// Separa los píxeles existentes; no repinta ni escala el dibujo.
// Las tres capas mantienen lienzo, registro y pigmento. Originales conservados para rollback.
import Foundation
import CoreGraphics
import ImageIO
import UniformTypeIdentifiers
let folder=URL(fileURLWithPath:FileManager.default.currentDirectoryPath).appendingPathComponent("Assets/StreamingAssets/Outfits/Traigger/Calabaza")
let space=CGColorSpace(name:CGColorSpace.sRGB)!
for side in ["Left","Right"] {
 let src=CGImageSourceCreateImageAtIndex(CGImageSourceCreateWithURL(folder.appendingPathComponent("PumpkinSleeve\(side).png") as CFURL,nil)!,0,nil)!,w=src.width,h=src.height
 var pixels=[UInt8](repeating:0,count:w*h*4)
 pixels.withUnsafeMutableBytes{raw in let ctx=CGContext(data:raw.baseAddress,width:w,height:h,bitsPerComponent:8,bytesPerRow:w*4,space:space,bitmapInfo:CGImageAlphaInfo.premultipliedLast.rawValue)!;ctx.draw(src,in:CGRect(x:0,y:0,width:w,height:h))}
 var cloth=pixels,cuff=pixels,rear=pixels
 for y in 0..<h {for x in 0..<w {
  let px=Double(side=="Left" ? x:w-1-x),py=Double(y)
  // Eje del puño, trazado sobre el contorno interior del dibujo (coordenadas desde arriba).
  let along=(px-151)*0.841+(py-326)*0.541
  let across = -(px-151)*0.541+(py-326)*0.841
  let arc=25*sqrt(max(0,1-pow(along/175,2)))
  let isRear=across > -arc+18
  // Límite naranja/puño. Se solapan sólo píxeles opacos para no dejar una costura de alfa.
  for k in 0..<4 {let i=(y*w+x)*4+k
   if isRear || across > -57 {cloth[i]=0}
   if isRear || across < -61 {cuff[i]=0}
   if !isRear {rear[i]=0}
  }
 }}
 func save(_ data:[UInt8],_ name:String){let cg=CGImage(width:w,height:h,bitsPerComponent:8,bitsPerPixel:32,bytesPerRow:w*4,space:space,bitmapInfo:CGBitmapInfo(rawValue:CGImageAlphaInfo.premultipliedLast.rawValue),provider:CGDataProvider(data:Data(data) as CFData)!,decode:nil,shouldInterpolate:true,intent:.defaultIntent)!;let out=CGImageDestinationCreateWithURL(folder.appendingPathComponent("PumpkinSleeve\(side)\(name).png") as CFURL,UTType.png.identifier as CFString,1,nil)!;CGImageDestinationAddImage(out,cg,nil);precondition(CGImageDestinationFinalize(out))}
 save(cloth,"Cloth");save(cuff,"Cuff");save(rear,"Lining")
 print("\(side): tela, borde delantero y forro; \(w)×\(h), registro original")
}
