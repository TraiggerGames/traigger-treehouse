#!/usr/bin/env ruby
# Adaptado de UnityRealityKitBridge para Traigger: host mínimo, sin modelos ni audio; la ventana de Unity queda visible.
# Usage: ruby integrate_host.rb /path/to/Unity-visionOS-build /path/to/NativeHost/Sources [debug|production] [version] [build]
# Runs once on a COPY of a fresh Unity Xcode build. Refuses an already integrated target.
require 'xcodeproj'
require 'fileutils'
require 'pathname'

build_dir, source_dir, mode, version, build = ARGV
mode ||= 'debug'
version = '1.0' if version.to_s.empty?
build = '1' if build.to_s.empty?
abort 'Mode must be debug or production' unless %w[debug production].include?(mode)
abort 'Pass Unity build directory and NativeHost/Sources directory' unless build_dir && source_dir
project_path = Dir[File.join(build_dir, '*.xcodeproj')].first or abort 'No .xcodeproj found'
project = Xcodeproj::Project.open(project_path)
abort 'NativeHost target already exists' if project.targets.any? { |target| target.name == 'NativeHost' }
unity_app = project.targets.find { |target| target.product_type == 'com.apple.product-type.application' }
unity_framework = project.targets.find { |target| target.name == 'UnityFramework' }
abort 'Expected Unity app and UnityFramework targets' unless unity_app && unity_framework

host_files = %w[TraiggerHostApp.swift TraiggerUnity.swift TraiggerSensors.swift TraiggerVoice.swift TraiggerSpeech.swift TraiggerPanelHost.swift TraiggerSpatial.swift TraiggerBodyReceiver.swift TraiggerMotionRelay.swift TraiggerHDMIReceiver.swift TraiggerShare.swift]
host_folder = File.join(build_dir, 'NativeHost')
FileUtils.mkdir_p(host_folder)
host_files.each do |filename|
  FileUtils.cp(File.join(source_dir, filename), File.join(host_folder, filename))
end
# Escucha (frases, emoción, sonidos): compartida con el plugin de iPhone/iPad.
shared_listen = File.expand_path('../../../Plugins/iOS/TraiggerListen.swift', source_dir)
FileUtils.cp(shared_listen, File.join(host_folder, 'TraiggerListen.swift'))
host_files += ['TraiggerListen.swift']
# Interfaz SwiftUI compartida con iPhone/iPad y Traigger Control (Assets/Traigger/Plugins/Apple/UI).
Dir[File.expand_path('../../../Plugins/Apple/UI/*.swift', source_dir)].sort.each do |file|
  FileUtils.cp(file, File.join(host_folder, File.basename(file)))
  host_files << File.basename(file)
end
File.write(File.join(host_folder, 'Info.plist'), <<~PLIST)
  <?xml version="1.0" encoding="UTF-8"?>
  <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
  <plist version="1.0"><dict>
    <key>CFBundleDisplayName</key><string>Traigger</string>
    <key>CFBundleExecutable</key><string>$(EXECUTABLE_NAME)</string>
    <key>CFBundleIdentifier</key><string>$(PRODUCT_BUNDLE_IDENTIFIER)</string>
    <key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
    <key>CFBundleName</key><string>$(PRODUCT_NAME)</string>
    <key>CFBundlePackageType</key><string>APPL</string>
    <key>CFBundleShortVersionString</key><string>#{version}</string>
    <key>CFBundleVersion</key><string>#{build}</string>
    <key>NSHandsTrackingUsageDescription</key><string>Tus manos mueven los brazos de Traigger.</string>
    <key>NSLocalNetworkUsageDescription</key><string>Recibe el cuerpo desde un iPhone o iPad y, si configuras un Mac, envía allí los movimientos de Traigger por tu red local.</string>
    <key>NSBonjourServices</key><array><string>_traigger-sensor._udp</string><string>_traigger-hdmi._tcp</string><string>_traigger-hdmiu._udp</string><string>_smb._tcp</string></array>
    <key>NSSpeechRecognitionUsageDescription</key><string>Traigger entiende tus frases en el propio casco para poner la expresión que les va. No se graba ni se envía nada.</string>
    <key>NSMicrophoneUsageDescription</key><string>El micrófono mueve la boca del personaje cuando hablas. El audio no se graba ni se envía.</string>
    <key>UIApplicationSceneManifest</key><dict><key>UIApplicationSupportsMultipleScenes</key><true/></dict>
  </dict></plist>
PLIST

# Perfil de voz, escenas y carteles en iCloud (opcional): el editor de Unity pasa el almacén clave-valor y el
# contenedor de iCloud Drive (imágenes y vídeos).
kvstore = ENV['TRAIGGER_KVSTORE'].to_s
container = ENV['TRAIGGER_ICLOUD_CONTAINER'].to_s
drive = container.empty? ? '' : "<key>com.apple.developer.icloud-container-identifiers</key><array><string>#{container}</string></array><key>com.apple.developer.ubiquity-container-identifiers</key><array><string>#{container}</string></array><key>com.apple.developer.icloud-services</key><array><string>CloudDocuments</string></array>"
unless kvstore.empty?
  File.write(File.join(host_folder, 'Traigger.entitlements'), <<~PLIST)
    <?xml version="1.0" encoding="UTF-8"?>
    <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
    <plist version="1.0"><dict>
      <key>com.apple.developer.ubiquity-kvstore-identifier</key><string>#{kvstore}</string>
      #{drive}
    </dict></plist>
  PLIST
end

host = project.new_target(:application, 'NativeHost', :visionos, '1.0')
group = project.main_group.new_group('NativeHost', 'NativeHost')
host_files.each do |filename|
  ref = group.new_file(filename)
  host.source_build_phase.add_file_reference(ref)
end
group.new_file('Info.plist')
host.add_dependency(unity_framework)
# Link the framework and embed the built product into the native app.
framework_ref = unity_framework.product_reference
host.frameworks_build_phase.add_file_reference(framework_ref)
embed = host.new_copy_files_build_phase('Embed UnityFramework')
embed.dst_subfolder_spec = '10' # Frameworks
item = embed.add_file_reference(framework_ref)
item.settings = { 'ATTRIBUTES' => ['CodeSignOnCopy', 'RemoveHeadersOnCopy'] }
# Todo lo que la app de Unity incrusta (UnityRuntime, VLCKit u otros plugins .framework) debe ir también en el host:
# UnityFramework los enlaza y, si faltan, la app se cierra al arrancar (dyld __abort_with_payload).
unity_app.copy_files_build_phases.select { |phase| phase.dst_subfolder_spec == '10' }.each do |phase|
  phase.files_references.each do |ref|
    next if ref.nil? || ref == framework_ref || embed.files_references.include?(ref)
    extra = embed.add_file_reference(ref)
    extra.settings = { 'ATTRIBUTES' => ['CodeSignOnCopy', 'RemoveHeadersOnCopy'] }
  end
end
# Unity's build stores Data in the application bundle, not in UnityFramework.
data_ref = project.files.find { |file| file.path == 'Data' }
abort 'Missing Unity Data folder reference' unless data_ref
host.resources_build_phase.add_file_reference(data_ref)
# The Unity Editor exporter places USDA models beside the Xcode project.
# A folder reference preserves the VisionModels subdirectory in the app.
host.build_configurations.each do |config|
  settings = config.build_settings
  unity_config = unity_app.build_configurations.find { |candidate| candidate.name == config.name }
  unity_settings = unity_config&.build_settings || {}
  settings['SDKROOT'] = 'xros'
  settings['SUPPORTED_PLATFORMS'] = 'xros xrsimulator'
  settings['TARGETED_DEVICE_FAMILY'] = '7'
  settings['XROS_DEPLOYMENT_TARGET'] = '2.0'
  settings['SWIFT_VERSION'] = '5.0'
  # Apple Intelligence (emoción por las palabras) solo existe en visionOS 26; en versiones anteriores se usa el respaldo.
  settings['OTHER_LDFLAGS'] = ['$(inherited)', '-weak_framework', 'FoundationModels']
  settings['INFOPLIST_FILE'] = 'NativeHost/Info.plist'
  # Unity Player Settings are the source of truth for signing. A developer
  # chooses team and bundle ID once in Unity; the host inherits both.
  settings['PRODUCT_BUNDLE_IDENTIFIER'] = unity_settings['PRODUCT_BUNDLE_IDENTIFIER'] || 'com.traigger.vtuberstudio'
  team = ENV['TRAIGGER_TEAM'].to_s.empty? ? unity_settings['DEVELOPMENT_TEAM'] : ENV['TRAIGGER_TEAM']
  settings['DEVELOPMENT_TEAM'] = team unless team.to_s.empty?
  settings['PRODUCT_NAME'] = 'Traigger'
  settings['LD_RUNPATH_SEARCH_PATHS'] = '$(inherited) @executable_path/Frameworks'
  settings['GENERATE_INFOPLIST_FILE'] = 'NO'
  settings['CODE_SIGN_STYLE'] = unity_settings['CODE_SIGN_STYLE'] || 'Automatic'
  settings['ENABLE_USER_SCRIPT_SANDBOXING'] = 'NO'
  settings['CODE_SIGN_ENTITLEMENTS'] = 'NativeHost/Traigger.entitlements' unless kvstore.empty?
end
# Firma fija en todos los objetivos (Signing.cs): Xcode no pide elegir equipo al abrir el proyecto.
unless ENV['TRAIGGER_TEAM'].to_s.empty?
  project.targets.each do |target|
    target.build_configurations.each do |config|
      config.build_settings['DEVELOPMENT_TEAM'] = ENV['TRAIGGER_TEAM']
      config.build_settings['CODE_SIGN_STYLE'] = 'Automatic'
    end
  end
end
project.save
puts "Added Traigger NativeHost #{version} (#{build}) to #{project_path}"
