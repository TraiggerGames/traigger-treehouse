// Native sprite slicing: curved contours, original pigment, registered pivots and hidden underlaps.
// Never modifies FrontHairHD.png. Run from the Unity project root.
import Foundation
import CoreGraphics
import ImageIO
import UniformTypeIdentifiers
let root=URL(fileURLWithPath:FileManager.default.currentDirectoryPath)
let folder=root.appendingPathComponent("Assets/Traigger/Resources/Hair")
let out=folder.appendingPathComponent("Strands")
try FileManager.default.createDirectory(at:out,withIntermediateDirectories:true)
let image=CGImageSourceCreateImageAtIndex(CGImageSourceCreateWithURL(folder.appendingPathComponent("FrontHairHD.png") as CFURL,nil)!,0,nil)!
let w=image.width,h=image.height,space=CGColorSpace(name:CGColorSpace.sRGB)!
var source=[UInt8](repeating:0,count:w*h*4)
source.withUnsafeMutableBytes{raw in let c=CGContext(data:raw.baseAddress,width:w,height:h,bitsPerComponent:8,bytesPerRow:w*4,space:space,bitmapInfo:CGImageAlphaInfo.premultipliedLast.rawValue)!;c.draw(image,in:CGRect(x:0,y:0,width:w,height:h))}
func smooth(_ a:Double,_ b:Double,_ x:Double)->Double{let t=max(0,min(1,(x-a)/(b-a)));return t*t*(3-2*t)}
struct Strand {
 let name:String,root:CGPoint,tip:CGPoint,angle:Double,mass:Double
 let contour:CGPath
 func progress(_ x:Int,_ y:Int)->Double{let dx=tip.x-root.x,dy=tip.y-root.y;return ((Double(x)-root.x)*dx+(Double(y)-root.y)*dy)/(dx*dx+dy*dy)}
}
func shape(_ build:(CGMutablePath)->Void)->CGPath{let p=CGMutablePath();build(p);p.closeSubpath();return p}
func m(_ p:CGMutablePath,_ x:Double,_ y:Double){p.move(to:CGPoint(x:x,y:y))}
func c(_ p:CGMutablePath,_ a:Double,_ b:Double,_ d:Double,_ e:Double,_ f:Double,_ g:Double){p.addCurve(to:CGPoint(x:f,y:g),control1:CGPoint(x:a,y:b),control2:CGPoint(x:d,y:e))}
var strands=[Strand]()
func add(_ n:String,_ rx:Double,_ ry:Double,_ tx:Double,_ ty:Double,_ angle:Double,_ mass:Double,_ path:CGPath){strands.append(Strand(name:n,root:CGPoint(x:rx,y:ry),tip:CGPoint(x:tx,y:ty),angle:angle,mass:mass,contour:path))}
// Front-to-back ownership follows the actual ink. Hidden overlap is added only behind a foreground lock.
add("Center",938,151,962,506,6.5,1.35,shape{p in
 m(p,938,144);c(p,904,164,882,207,873,263);c(p,855,329,870,400,899,446);c(p,918,479,944,501,965,515)
 c(p,958,478,987,444,1005,386);c(p,1026,323,1016,252,1000,210);c(p,984,170,959,149,938,144)
})
add("BrownLeft",894,168,877,448,7,1.05,shape{p in
 m(p,914,153);c(p,871,165,841,208,825,264);c(p,806,323,810,382,828,413);c(p,842,433,860,447,882,453)
 c(p,851,410,840,361,849,308);c(p,858,247,878,196,914,153)
})
add("BrownRight",989,166,1000,451,7,1.1,shape{p in
 m(p,959,151);c(p,1007,154,1047,193,1066,247);c(p,1085,309,1072,383,1046,420);c(p,1028,442,1011,452,995,457)
 c(p,1027,409,1039,350,1032,291);c(p,1020,239,998,188,959,151)
})
add("CyanInnerLeft",851,181,802,449,8,0.85,shape{p in
 m(p,889,153);c(p,840,175,808,215,787,267);c(p,764,320,760,386,809,459)
 c(p,803,433,797,380,806,329);c(p,796,269,826,203,889,153)
})
add("CyanInnerRight",1044,191,1069,457,8,0.88,shape{p in
 m(p,1021,170);c(p,1068,186,1103,224,1121,276);c(p,1138,334,1119,407,1060,469)
 c(p,1095,398,1106,346,1089,294);c(p,1074,247,1046,209,1021,170)
})
add("SweepLeft",813,198,787,504,7.5,1.15,shape{p in
 m(p,865,153);c(p,797,174,751,220,721,287);c(p,695,347,700,416,734,461);c(p,750,481,772,496,797,511)
 c(p,758,462,744,415,750,361);c(p,756,289,794,210,865,153)
})
add("SweepRight",1070,198,1097,510,7.5,1.15,shape{p in
 m(p,1015,155);c(p,1088,170,1141,210,1172,255);c(p,1194,305,1184,371,1154,419);c(p,1135,450,1116,481,1090,515)
 c(p,1130,460,1148,406,1147,350);c(p,1145,280,1095,203,1015,155)
})
// Each lower lock follows one actual pointed silhouette, not a straight strip through the blue gradient.
let leftInner=shape{p in m(p,747,280);c(p,710,330,690,400,690,470);c(p,690,540,730,630,809,703);c(p,780,620,768,550,766,490);c(p,765,430,760,350,747,280)}
let leftMiddle=shape{p in m(p,730,260);c(p,690,310,665,370,663,450);c(p,655,520,680,598,746,666);c(p,727,615,709,566,700,516);c(p,685,447,697,354,730,260)}
let leftOuter=shape{p in m(p,716,250);c(p,679,285,650,357,641,428);c(p,635,472,644,498,662,505);c(p,647,452,668,374,691,327);c(p,701,293,711,265,716,250)}
let mirror=CGAffineTransform(a:-1,b:0,c:0,d:1,tx:1884,ty:0)
func mirrored(_ p:CGPath)->CGPath{var t=mirror;return p.copy(using:&t)!}
add("SideInnerLeft",746,291,801,691,8.5,1.3,leftInner)
add("SideMiddleLeft",724,287,737,650,8,1.35,leftMiddle)
add("SideOuterLeft",711,279,655,490,7,0.9,leftOuter)
add("SideInnerRight",1140,291,1092,691,8.5,1.3,mirrored(leftInner))
add("SideMiddleRight",1161,287,1147,650,8,1.35,mirrored(leftMiddle))
add("SideOuterRight",1173,279,1229,490,7,0.9,mirrored(leftOuter))
// Four independently drawn crown wisps, rooted beneath the common cap.
add("CrownLeft",850,169,704,233,6,0.55,shape{p in m(p,949,130);c(p,853,102,747,151,687,242);c(p,756,193,817,174,885,170);c(p,909,160,932,147,949,130)})
add("CrownWispLeft",936,145,820,99,8,0.38,shape{p in m(p,951,150);c(p,923,111,868,86,811,99);c(p,867,109,902,135,923,165);c(p,936,166,946,159,951,150)})
add("CrownWispRight",938,145,1046,82,8,0.38,shape{p in m(p,920,157);c(p,951,105,1003,77,1056,80);c(p,1000,94,971,126,957,162);c(p,943,173,927,171,920,157)})
add("CrownRight",1032,169,1186,232,6,0.55,shape{p in m(p,941,135);c(p,1039,105,1142,149,1204,241);c(p,1137,193,1074,175,1007,170);c(p,981,161,959,148,941,135)})
let count=w*h
func raster(_ path:CGPath)->[UInt8]{var b=[UInt8](repeating:0,count:count);b.withUnsafeMutableBytes{raw in let ctx=CGContext(data:raw.baseAddress,width:w,height:h,bitsPerComponent:8,bytesPerRow:w,space:CGColorSpaceCreateDeviceGray(),bitmapInfo:0)!;ctx.translateBy(x:0,y:CGFloat(h));ctx.scaleBy(x:1,y:-1);ctx.setFillColor(gray:1,alpha:1);ctx.addPath(path);ctx.fillPath()};return b}
let masks=strands.map{raster($0.contour)}
let priority=[0,3,4,1,2,5,6,7,10,8,11,9,12,14,15,13,16]
var owner=[Int16](repeating:-1,count:count)
// Each source pixel belongs to one visible strand. A root collar is left in the cap.
for y in 68..<708 {for x in 630..<1260 {
 let i=y*w+x
 if source[i*4+3]==0 {continue}
 for k in priority where masks[k][i]>127 {
  if strands[k].progress(x,y)<0.01 {continue}
  owner[i]=Int16(k);break
 }
}}
// Assign any subpixel ink slivers below the roots to their nearest neighbouring strand.
var fillQueue=[Int]();for y in 300..<708{for x in 630..<1260{let i=y*w+x;if owner[i]>=0{fillQueue.append(i)}}}
var fillCursor=0
while fillCursor<fillQueue.count {let i=fillQueue[fillCursor];fillCursor+=1;let x=i%w,y=i/w
 for j in [i-1,i+1,i-w,i+w] where j>=0 && j<count {let nx=j%w,ny=j/w;if nx<630 || nx>=1260 || ny<300 || ny>=708 || abs(nx-x)+abs(ny-y) != 1{continue};if owner[j]<0 && source[j*4+3]>0{owner[j]=owner[i];fillQueue.append(j)}}
}
// Keep each connected cyan highlight on the same strand; previously the sweep's end was
// assigned to a side lock, leaving a detached blue remnant behind when it moved.
var visited=[Bool](repeating:false,count:count)
func cyan(_ i:Int)->Bool{let j=i*4;return source[j+3]>100 && Int(source[j+2])-Int(source[j])>45 && Int(source[j+1])-Int(source[j])>40}
for y in 180..<700 {for x in 650..<1245 {let start=y*w+x;if visited[start] || !cyan(start){continue};var component=[start],cursor=0;visited[start]=true
 while cursor<component.count {let i=component[cursor];cursor+=1;for j in [i-1,i+1,i-w,i+w] where j>=0 && j<count {if !visited[j] && cyan(j){visited[j]=true;component.append(j)}}}
 if component.count<100{continue}
 let xs=component.map{$0%w},ys=component.map{$0/w};let minX=xs.min()!,minY=ys.min()!,maxX=xs.max()!,maxY=ys.max()!
 let k:Int
 if minY<230{k=minX>850 ? 0:3}
 else if minY<340{k=minX<850 ? 5:(maxX>1140 ? 6:4)}
 else if minY<430{k=minX<850 ? 3:4}
 else if minY<480{continue} // Long lower dye crosses two naturally overlapping tips; traced ink splits it.
 else{k=minX<850 ? 7:10}
 for i in component{owner[i]=Int16(k)}
 // Include dark antialias/ink immediately bordering this highlight, not another coloured component.
 for i in component{for dy in -2...2{for dx in -2...2{let j=i+dy*w+dx;if j>=0 && j<count && source[j*4+3]>180 && !cyan(j){owner[j]=Int16(k)}}}}
 _=maxY
}}
func save(_ b:[UInt8],_ width:Int,_ height:Int,_ url:URL){let im=CGImage(width:width,height:height,bitsPerComponent:8,bitsPerPixel:32,bytesPerRow:width*4,space:space,bitmapInfo:CGBitmapInfo(rawValue:CGImageAlphaInfo.premultipliedLast.rawValue),provider:CGDataProvider(data:Data(b) as CFData)!,decode:nil,shouldInterpolate:true,intent:.defaultIntent)!;let d=CGImageDestinationCreateWithURL(url as CFURL,UTType.png.identifier as CFString,1,nil)!;CGImageDestinationAddImage(d,im,nil);CGImageDestinationFinalize(d)}
var base=source,entries=[[String:Any]]()
var labels=[UInt8](repeating:0,count:source.count)
// The narrow seam backing is deformed by exactly the same field as every sprite.
// It covers filtering between complementary masks, never leaves an unmoving copy behind.
for y in 68..<708{for x in 630..<1260{
 let i=y*w+x,k=Int(owner[i]);labels[i*4]=UInt8(k+1);labels[i*4+3]=255
 if k<0{continue}
 let t=strands[k].progress(x,y)
 var seam=false,exterior=source[i*4+3]<255
 for dy in [-4,0,4]{for dx in [-4,0,4]{let j=i+dy*w+dx;if j<0 || j>=count{continue};if source[j*4+3]<255{exterior=true};if owner[j]>=0 && owner[j] != owner[i]{seam=true}}}
 // Full coverage at all filtering boundaries and at the immobile root collar. The broad
 // interiors belong to the separate sprites, so this backing is not a second complete hair image.
 let retain=(seam || exterior || t<0.24) ? 1.0:0.0
 for ch in 0..<4{base[i*4+ch]=UInt8((Double(source[i*4+ch])*retain).rounded())}
}}
save(base,w,h,folder.appendingPathComponent("FrontHairBaseHD.png"))
save(labels,w,h,folder.appendingPathComponent("FrontHairOwnership.png"))
for k in strands.indices{
 let strand=strands[k];var x0=w,y0=h,x1=0,y1=0,owned=0
 for y in 68..<708{for x in 630..<1260{let i=y*w+x;if Int(owner[i])==k{owned+=1;x0=min(x0,x);y0=min(y0,y);x1=max(x1,x);y1=max(y1,y)}}}
 precondition(owned>30,"Empty strand: \(strand.name)")
 let ownedRect=[x0,y0,x1-x0+1,y1-y0+1]
 x0=max(0,x0-12);y0=max(0,y0-12);x1=min(w,x1+13);y1=min(h,y1+13)
 let cw=x1-x0,ch=y1-y0;var crop=[UInt8](repeating:0,count:cw*ch*4)
 for y in 0..<ch{for x in 0..<cw{
  let sx=x+x0,sy=y+y0,i=sy*w+sx,j=(y*cw+x)*4
  if Int(owner[i]) != k{continue}
  let fade=smooth(0.015,0.20,strand.progress(sx,sy))
  for ch in 0..<4{crop[j+ch]=UInt8((Double(source[i*4+ch])*fade).rounded())}
 }}
 save(crop,cw,ch,out.appendingPathComponent("FrontHair\(strand.name).png"))
 entries.append(["name":strand.name,"resource":"Hair/Strands/FrontHair\(strand.name)","rect":[x0,y0,cw,ch],"root":[strand.root.x,strand.root.y],"tip":[strand.tip.x,strand.tip.y],"angle":strand.angle,"mass":strand.mass,"order":strands.count-priority.firstIndex(of:k)!,"crown":k>=13,"ownedPixels":owned,"visibleRect":ownedRect])
 print("\(strand.name): \(owned) visible pixels, \(cw)x\(ch)")
}
let report:[String:Any]=["version":3,"sourceSize":[w,h],"strands":entries,"note":"17 source-pixel sprites; shared continuous seam deformation; 17 body springs and 17 tip springs; 4 crown wisps"]
try JSONSerialization.data(withJSONObject:report,options:[.prettyPrinted,.sortedKeys]).write(to:folder.appendingPathComponent("FrontHairLayers.json"))
print("Generated \(strands.count) independent registered sprite assets.")
