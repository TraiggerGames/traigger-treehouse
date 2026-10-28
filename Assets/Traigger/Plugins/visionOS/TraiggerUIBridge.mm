#import <Foundation/Foundation.h>

// Vision Pro: puente entre Unity (TraiggerBridge.cs) y la interfaz SwiftUI del host nativo (TraiggerPanelHost.swift).
// Unity y el host comparten proceso, así que se hablan con avisos internos:
//   «Traigger.State»   Unity → host, con el estado en JSON;
//   «Traigger.Command» host → Unity, con una orden en JSON.
typedef void (*TraiggerCommandCallback)(const char *json);
static TraiggerCommandCallback commandCallback;
static id observer;

extern "C" void TraiggerUIRegister(TraiggerCommandCallback callback) {
    commandCallback = callback;
    if (observer) return;
    observer = [[NSNotificationCenter defaultCenter] addObserverForName:@"Traigger.Command" object:nil queue:[NSOperationQueue mainQueue] usingBlock:^(NSNotification *note) {
        NSString *json = note.userInfo[@"json"];
        if (commandCallback && json) commandCallback(json.UTF8String);
    }];
}

extern "C" void TraiggerUIPushState(const char *json) {
    if (!json) return;
    NSString *text = [NSString stringWithUTF8String:json];
    dispatch_async(dispatch_get_main_queue(), ^{
        [[NSNotificationCenter defaultCenter] postNotificationName:@"Traigger.State" object:nil userInfo:@{@"json": text}];
    });
}

// Imagen de Traigger con fondo transparente (AvatarOverlayFeed.cs, RGBA de abajo arriba). Se recorta a la silueta del
// personaje (con un margen y suavizado, para que no tiemble) y se pasa al host como CGImage en «Traigger.Avatar»:
// la usan Traigger «libre» y la ventana para compartir (TraiggerShare.swift).
#import <CoreGraphics/CoreGraphics.h>
static CGRect smoothedCrop;
extern "C" void TraiggerAvatarOverlayFrame(const uint8_t *pixels, int width, int height) {
    if (!pixels || width <= 0 || height <= 0) return;
    // Silueta: zona con alfa, muestreada cada 4 píxeles.
    int minX = width, minY = height, maxX = -1, maxY = -1;
    for (int y = 0; y < height; y += 4) {
        const uint8_t *row = pixels + (size_t)y * width * 4;
        for (int x = 0; x < width; x += 4) if (row[x * 4 + 3] > 8) { if (x < minX) minX = x; if (x > maxX) maxX = x; if (y < minY) minY = y; if (y > maxY) maxY = y; }
    }
    if (maxX < 0) return;
    int margin = MAX(width, height) / 40;
    CGRect crop = CGRectMake(MAX(0, minX - margin), MAX(0, minY - margin), 0, 0);
    crop.size.width = MIN(width, maxX + margin + 4) - crop.origin.x; crop.size.height = MIN(height, maxY + margin + 4) - crop.origin.y;
    if (CGRectIsEmpty(smoothedCrop) || !CGRectIntersectsRect(smoothedCrop, crop)) smoothedCrop = crop;
    else {
        // Crece al instante y encoge despacio: el encuadre no da saltos con cada gesto.
        CGRect grown = CGRectUnion(smoothedCrop, crop);
        CGFloat k = 0.08;
        smoothedCrop = CGRectMake(grown.origin.x + (crop.origin.x - grown.origin.x) * k, grown.origin.y + (crop.origin.y - grown.origin.y) * k,
                                  grown.size.width + (crop.size.width - grown.size.width) * k, grown.size.height + (crop.size.height - grown.size.height) * k);
    }
    CGRect c = CGRectIntegral(CGRectIntersection(smoothedCrop, CGRectMake(0, 0, width, height)));
    int cw = (int)c.size.width, ch = (int)c.size.height;
    if (cw <= 0 || ch <= 0) return;
    // Copia volteada (de arriba abajo) solo de la zona recortada.
    NSMutableData *data = [NSMutableData dataWithLength:(size_t)cw * ch * 4];
    uint8_t *out = (uint8_t *)data.mutableBytes;
    for (int y = 0; y < ch; y++) {
        int source = (int)c.origin.y + (ch - 1 - y);
        memcpy(out + (size_t)y * cw * 4, pixels + ((size_t)source * width + (size_t)c.origin.x) * 4, (size_t)cw * 4);
    }
    CGDataProviderRef provider = CGDataProviderCreateWithCFData((__bridge CFDataRef)data);
    CGColorSpaceRef space = CGColorSpaceCreateDeviceRGB();
    CGImageRef image = CGImageCreate(cw, ch, 8, 32, (size_t)cw * 4, space, kCGBitmapByteOrderDefault | kCGImageAlphaLast, provider, NULL, false, kCGRenderingIntentDefault);
    CGColorSpaceRelease(space); CGDataProviderRelease(provider);
    if (!image) return;
    id object = (__bridge_transfer id)image;
    dispatch_async(dispatch_get_main_queue(), ^{
        [[NSNotificationCenter defaultCenter] postNotificationName:@"Traigger.Avatar" object:object];
    });
}
