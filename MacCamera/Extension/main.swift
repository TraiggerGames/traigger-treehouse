import CoreMediaIO
import Foundation

// Cámara virtual «Traigger»: la ve FaceTime (y Zoom, Meet, Discord…) como una webcam más.
let providerSource = TraiggerProviderSource(clientQueue: nil)
CMIOExtensionProvider.startService(provider: providerSource.provider)
CFRunLoopRun()
