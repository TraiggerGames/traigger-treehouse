#!/usr/bin/env ruby
# Genera TraiggerControl.xcodeproj (app de Mac) con la interfaz compartida de Assets/Traigger/Plugins/Apple/UI.
require 'xcodeproj'
require 'fileutils'
# Equipo de firma: TRAIGGER_TEAM del entorno o de signing.local (no se sube al repositorio).
local = File.join(__dir__, '../../signing.local')
local = File.join(__dir__, '../signing.local') unless File.exist?(local)
team = ENV['TRAIGGER_TEAM'] || (File.exist?(local) ? File.read(local)[/TRAIGGER_TEAM="([^"]+)"/, 1] : nil) || ''
path = File.join(__dir__, 'TraiggerControl.xcodeproj')
FileUtils.rm_rf(path)
project = Xcodeproj::Project.new(path)
app = project.new_target(:application, 'Traigger Control', :osx, '13.0')
group = project.main_group.new_group('Control', '.')
app.source_build_phase.add_file_reference(group.new_file('TraiggerControlApp.swift'))
ui = project.main_group.new_group('UI', '../../Assets/Traigger/Plugins/Apple/UI')
Dir[File.join(__dir__, '../../Assets/Traigger/Plugins/Apple/UI/*.swift')].sort.each { |f| app.source_build_phase.add_file_reference(ui.new_file(File.basename(f))) }
app.build_configurations.each do |config|
  s = config.build_settings
  s['PRODUCT_BUNDLE_IDENTIFIER'] = 'com.traigger.control'
  s['INFOPLIST_FILE'] = 'Info.plist'
  s['GENERATE_INFOPLIST_FILE'] = 'NO'
  s['DEVELOPMENT_TEAM'] = team
  s['CODE_SIGN_STYLE'] = 'Automatic'
  s['MACOSX_DEPLOYMENT_TARGET'] = '13.0'
  s['SWIFT_VERSION'] = '5.0'
  s['ENABLE_HARDENED_RUNTIME'] = 'YES'
  s['PRODUCT_NAME'] = 'Traigger Control'
end
project.save
puts "Creado #{path}"
