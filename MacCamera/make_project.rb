#!/usr/bin/env ruby
# Genera TraiggerCamera.xcodeproj: la app «Traigger Camera» con la extensión de cámara dentro, firmada con el equipo
# de Signing.cs. Uso: ruby make_project.rb (desde esta carpeta). Se puede regenerar cuando se quiera.
require 'xcodeproj'
require 'fileutils'
# Equipo de firma: TRAIGGER_TEAM del entorno o de signing.local (no se sube al repositorio).
local = File.join(__dir__, '../../signing.local')
local = File.join(__dir__, '../signing.local') unless File.exist?(local)
team = ENV['TRAIGGER_TEAM'] || (File.exist?(local) ? File.read(local)[/TRAIGGER_TEAM="([^"]+)"/, 1] : nil) || ''
path = File.join(__dir__, 'TraiggerCamera.xcodeproj')
FileUtils.rm_rf(path)
project = Xcodeproj::Project.new(path)

app = project.new_target(:application, 'Traigger Camera', :osx, '13.0')
ext = project.new_target(:application, 'TraiggerCameraExtension', :osx, '13.0')
ext.product_type = 'com.apple.product-type.system-extension'
ext.product_reference.path = 'com.traigger.camera.extension.systemextension'
ext.product_reference.explicit_file_type = 'wrapper.system-extension'

app_group = project.main_group.new_group('App', 'App')
%w[TraiggerCameraApp.swift UsbMux.swift CableBridge.swift AudioMixer.swift HDMIStation.swift FaceForwarder.swift Stubs.c].each { |f| app.source_build_phase.add_file_reference(app_group.new_file(f)) }
# Capturadora HDMI y envío de baja latencia: los mismos archivos que usa la app del iPad.
plugins_group = project.main_group.new_group('Plugins', '../Assets/Traigger/Plugins/Apple')
%w[TraiggerCapture.swift TraiggerStream.swift TraiggerLink.swift].each { |f| app.source_build_phase.add_file_reference(plugins_group.new_file(f)) }
# El emisor hacia la entrada de la cámara es el mismo que usa el plugin de Unity para Mac.
sender_group = project.main_group.new_group('Sender', 'Sender')
app.source_build_phase.add_file_reference(sender_group.new_file('TraiggerVirtualCam.swift'))
# Formato y decodificador del vídeo del iPhone, compartidos por la app (cable) y la extensión (Wi-Fi).
shared_group = project.main_group.new_group('Shared', 'Shared')
shared_ref = shared_group.new_file('TraiggerVideo.swift')
app.source_build_phase.add_file_reference(shared_ref)
ext.source_build_phase.add_file_reference(shared_ref)
ext_group = project.main_group.new_group('Extension', 'Extension')
%w[main.swift TraiggerCamera.swift PhoneReceiver.swift].each { |f| ext.source_build_phase.add_file_reference(ext_group.new_file(f)) }

app.add_dependency(ext)
embed = app.new_copy_files_build_phase('Embed System Extensions')
embed.dst_subfolder_spec = '1' # Wrapper
embed.dst_path = 'Contents/Library/SystemExtensions'
embed.add_file_reference(ext.product_reference).settings = { 'ATTRIBUTES' => ['RemoveHeadersOnCopy'] }

{ app => ['com.traigger.camera', 'App/Info.plist', 'App/App.entitlements'],
  ext => ['com.traigger.camera.extension', 'Extension/Info.plist', 'Extension/Extension.entitlements'] }.each do |target, (id, plist, rights)|
  target.build_configurations.each do |config|
    s = config.build_settings
    s['PRODUCT_BUNDLE_IDENTIFIER'] = id
    s['INFOPLIST_FILE'] = plist
    s['CODE_SIGN_ENTITLEMENTS'] = rights
    s['DEVELOPMENT_TEAM'] = team
    s['CODE_SIGN_STYLE'] = 'Automatic'
    s['MACOSX_DEPLOYMENT_TARGET'] = '13.0'
    s['SWIFT_VERSION'] = '5.0'
    s['ENABLE_HARDENED_RUNTIME'] = 'YES'
    s['GENERATE_INFOPLIST_FILE'] = 'NO'
    s['PRODUCT_NAME'] = target == ext ? 'com.traigger.camera.extension' : 'Traigger Camera'
  end
end
# El dispositivo de audio «Traigger» va dentro de la app para instalarlo desde su menú (AudioDriver/build.sh).
driver_ref = project.main_group.new_file('AudioDriver/build/TraiggerAudio.driver')
driver_ref.last_known_file_type = 'wrapper.cfbundle'
app.resources_build_phase.add_file_reference(driver_ref)
project.save
puts "Creado #{path}"
