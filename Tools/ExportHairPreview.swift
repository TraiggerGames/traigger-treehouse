// Exports the offline Unity rig-validation frames; not a recording of live Face ID input.
import Foundation
import CoreGraphics
import ImageIO
import UniformTypeIdentifiers
let root=URL(fileURLWithPath:FileManager.default.currentDirectoryPath)
let folder=root.appendingPathComponent("Validation/Hair/Strands17/Frames")
let files=try FileManager.default.contentsOfDirectory(at:folder,includingPropertiesForKeys:nil).filter{$0.pathExtension=="png"}.sorted{$0.lastPathComponent<$1.lastPathComponent}
let path=root.appendingPathComponent("Validation/Hair/Triangles/movement.gif")
let out=CGImageDestinationCreateWithURL(path as CFURL,UTType.gif.identifier as CFString,files.count,nil)!
CGImageDestinationSetProperties(out,[kCGImagePropertyGIFDictionary:[kCGImagePropertyGIFLoopCount:0]] as CFDictionary)
for file in files {
 let im=CGImageSourceCreateImageAtIndex(CGImageSourceCreateWithURL(file as CFURL,nil)!,0,nil)!
 let ctx=CGContext(data:nil,width:480,height:600,bitsPerComponent:8,bytesPerRow:0,space:CGColorSpaceCreateDeviceRGB(),bitmapInfo:CGImageAlphaInfo.noneSkipLast.rawValue)!
 ctx.setFillColor(CGColor(red:16/255,green:17/255,blue:30/255,alpha:1));ctx.fill(CGRect(x:0,y:0,width:480,height:600));ctx.interpolationQuality = .high
 ctx.draw(im,in:CGRect(x:0,y:0,width:480,height:600))
 CGImageDestinationAddImage(out,ctx.makeImage()!,[kCGImagePropertyGIFDictionary:[kCGImagePropertyGIFDelayTime:1.0/30,kCGImagePropertyGIFUnclampedDelayTime:1.0/30]] as CFDictionary)
}
precondition(CGImageDestinationFinalize(out));print("Exported \(files.count) Unity validation frames")
