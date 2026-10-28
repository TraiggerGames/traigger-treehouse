// Seguimiento facial ARKit para Traigger (iPhone/iPad con cámara TrueDepth o chip A12 o posterior).
// Se ejecuta una ARSession con ARFaceTrackingConfiguration sin fondo de cámara: Unity sigue dibujando el avatar
// y aquí sólo se leen la pose de la cabeza y los coeficientes de expresión. Los datos se copian bajo un cerrojo
// y Unity los lee una vez por fotograma con TraiggerFaceRead.
#import <ARKit/ARKit.h>
#import <AVFoundation/AVFoundation.h>
#import <UIKit/UIKit.h>
#include <math.h>
#include <stdio.h>

static const int kValueCount = 40;
static float gValues[kValueCount];
static bool gHasNew = false;
static bool gDenied = false;
static bool gInterrupted = false;
// Mientras la capturadora usa las cámaras, la cara llega por Vision: ARKit se pausa y no intenta recuperar la cámara
// (si lo hace, le quita la cámara a la captura y se pelean).
static bool gExternal = false;
static bool gCapturePriority = false;
static bool gHybrid = false;
static bool gRequested = false;
static bool gLocalProbe = false, gProbeFailed = false;
static double gCameraFrameAt = 0, gStartedAt = 0;
static unsigned long gFrameCount = 0;
static NSInteger gErrorCode = 0;
static NSString *gErrorText = @"";
extern "C" int TraiggerFaceStart(void);
extern "C" int TraiggerFaceResumeRequested(void);
static NSLock *gLock = nil;

@interface TraiggerFaceTracker : NSObject <ARSessionDelegate>
@property (nonatomic, strong) ARSession *session;
@end

static TraiggerFaceTracker *gTracker = nil;

static UIInterfaceOrientation CurrentOrientation(void) {
    UIInterfaceOrientation orientation = UIInterfaceOrientationPortrait;
    for (UIScene *scene in UIApplication.sharedApplication.connectedScenes) {
        if ([scene isKindOfClass:[UIWindowScene class]]) {
            orientation = ((UIWindowScene *)scene).interfaceOrientation;
            break;
        }
    }
    return orientation == UIInterfaceOrientationUnknown ? UIInterfaceOrientationPortrait : orientation;
}

static float Shape(ARFaceAnchor *face, ARBlendShapeLocation key) {
    NSNumber *value = face.blendShapes[key];
    return value ? value.floatValue : 0.0f;
}

@implementation TraiggerFaceTracker
- (void)session:(ARSession *)session didUpdateFrame:(ARFrame *)frame {
    [gLock lock];
    if (gRequested && !gExternal) { gInterrupted = false; gCameraFrameAt = NSProcessInfo.processInfo.systemUptime; gFrameCount++; }
    [gLock unlock];
}
- (void)session:(ARSession *)session didUpdateAnchors:(NSArray<__kindof ARAnchor *> *)anchors {
    [gLock lock]; bool accept = gRequested && !gExternal && !gInterrupted; [gLock unlock];
    if (!accept) return;
    ARFrame *frame = session.currentFrame;
    if (!frame) return;
    for (ARAnchor *anchor in anchors) {
        if (![anchor isKindOfClass:[ARFaceAnchor class]]) continue;
        ARFaceAnchor *face = (ARFaceAnchor *)anchor;
        // Sin cara rastreada (o sin imagen de la cámara) no se publica nada: Unity pasa a la voz y al reposo en vez de
        // quedarse con la última pose congelada.
        if (!face.isTracked) continue;
        // Pose de la cara en el espacio de la vista según la orientación de la pantalla (x derecha, y arriba,
        // z hacia el usuario). Mirando a la pantalla, el eje z de la cara apunta hacia la cámara.
        simd_float4x4 view = [frame.camera viewMatrixForOrientation:CurrentOrientation()];
        simd_float4x4 relative = simd_mul(view, face.transform);
        simd_float3 forward = simd_make_float3(relative.columns[2].x, relative.columns[2].y, relative.columns[2].z);
        simd_float3 up = simd_make_float3(relative.columns[1].x, relative.columns[1].y, relative.columns[1].z);
        const float toDegrees = 57.29578f;
        float values[kValueCount];
        values[0] = atan2f(forward.x, forward.z) * toDegrees;               // giro: positivo hacia la izquierda del usuario
        values[1] = asinf(fmaxf(-1.0f, fminf(1.0f, forward.y))) * toDegrees; // positivo mirando arriba
        values[2] = atan2f(-up.x, up.y) * toDegrees;                         // inclinación
        values[3] = Shape(face, ARBlendShapeLocationJawOpen);
        values[4] = Shape(face, ARBlendShapeLocationEyeBlinkLeft);
        values[5] = Shape(face, ARBlendShapeLocationEyeBlinkRight);
        values[6] = Shape(face, ARBlendShapeLocationMouthSmileLeft);
        values[7] = Shape(face, ARBlendShapeLocationMouthSmileRight);
        values[8] = Shape(face, ARBlendShapeLocationEyeLookInLeft);
        values[9] = Shape(face, ARBlendShapeLocationEyeLookOutLeft);
        values[10] = Shape(face, ARBlendShapeLocationEyeLookInRight);
        values[11] = Shape(face, ARBlendShapeLocationEyeLookOutRight);
        values[12] = Shape(face, ARBlendShapeLocationEyeLookUpLeft);
        values[13] = Shape(face, ARBlendShapeLocationEyeLookUpRight);
        values[14] = Shape(face, ARBlendShapeLocationEyeLookDownLeft);
        values[15] = Shape(face, ARBlendShapeLocationEyeLookDownRight);
        values[16] = face.isTracked ? 1.0f : 0.0f;
        values[17] = Shape(face, ARBlendShapeLocationMouthFunnel);
        values[18] = Shape(face, ARBlendShapeLocationMouthPucker);
        values[19] = Shape(face, ARBlendShapeLocationBrowInnerUp);
        values[20] = Shape(face, ARBlendShapeLocationBrowDownLeft);
        values[21] = Shape(face, ARBlendShapeLocationBrowDownRight);
        values[22] = Shape(face, ARBlendShapeLocationBrowOuterUpLeft);
        values[23] = Shape(face, ARBlendShapeLocationBrowOuterUpRight);
        values[24] = Shape(face, ARBlendShapeLocationEyeWideLeft);
        values[25] = Shape(face, ARBlendShapeLocationEyeWideRight);
        values[26] = Shape(face, ARBlendShapeLocationEyeSquintLeft);
        values[27] = Shape(face, ARBlendShapeLocationEyeSquintRight);
        values[28] = Shape(face, ARBlendShapeLocationCheekPuff);
        values[29] = Shape(face, ARBlendShapeLocationCheekSquintLeft);
        values[30] = Shape(face, ARBlendShapeLocationCheekSquintRight);
        values[31] = Shape(face, ARBlendShapeLocationMouthFrownLeft);
        values[32] = Shape(face, ARBlendShapeLocationMouthFrownRight);
        values[33] = Shape(face, ARBlendShapeLocationMouthLeft);
        values[34] = Shape(face, ARBlendShapeLocationMouthRight);
        values[35] = Shape(face, ARBlendShapeLocationMouthPressLeft);
        values[36] = Shape(face, ARBlendShapeLocationMouthPressRight);
        values[37] = Shape(face, ARBlendShapeLocationNoseSneerLeft);
        values[38] = Shape(face, ARBlendShapeLocationNoseSneerRight);
        values[39] = Shape(face, ARBlendShapeLocationTongueOut);
        [gLock lock];
        memcpy(gValues, values, sizeof(values));
        gHasNew = true;
        [gLock unlock];
    }
}
// La cámara de cara se interrumpe si otra captura la ocupa (p. ej. la capturadora HDMI por USB-C): se avisa y, al
// acabar la interrupción, se vuelve a arrancar el seguimiento.
- (void)sessionWasInterrupted:(ARSession *)session {
    [gLock lock]; gInterrupted = true; gHasNew = false; if (gLocalProbe) gProbeFailed = true; [gLock unlock];
    NSLog(@"Traigger: ARKit notificó una interrupción; causa aún no determinada");
}
- (void)sessionInterruptionEnded:(ARSession *)session {
    [gLock lock]; gInterrupted = false; bool external = gExternal; bool probe = gLocalProbe; [gLock unlock];
    if (probe) return; // USB se detendrá antes de recuperar ARKit: no iniciar una pelea entre sesiones.
    if (external) { [session pause]; return; }
    NSLog(@"Traigger: seguimiento de cara reanudado");
    dispatch_async(dispatch_get_main_queue(), ^{ TraiggerFaceResumeRequested(); });
}
- (void)session:(ARSession *)session didFailWithError:(NSError *)error {
    [gLock lock]; gErrorCode = error.code; if (gLocalProbe) gProbeFailed = true; gErrorText = [NSString stringWithFormat:@"%@: %@", error.domain, error.localizedDescription]; gHasNew = false;
    if (error.code == ARErrorCodeCameraUnauthorized) gDenied = true;
    [gLock unlock];
    NSLog(@"Traigger: error ARKit %ld · %@", (long)error.code, error);
}
@end

extern "C" {
    // Cara seguida por otro medio (Vision con la cámara frontal mientras la capturadora ocupa las cámaras).
    void TraiggerFacePushExternal(const float *values, int count) {
        if (!gLock) gLock = [[NSLock alloc] init];
        [gLock lock];
        for (int i = 0; i < kValueCount && i < count; i++) gValues[i] = values[i];
        gHasNew = true;
        [gLock unlock];
    }
    // La capturadora avisa al empezar (1) y al terminar (0): ARKit se pausa y luego vuelve.
    void TraiggerFaceSetExternal(int on) {
        if (!gLock) gLock = [[NSLock alloc] init];
        [gLock lock]; gExternal = on != 0; gInterrupted = on != 0; gHasNew = false; [gLock unlock];
        void (^transition)(void) = ^{
            [gLock lock]; bool current = gExternal == (on != 0); [gLock unlock];
            if (!current) return; // Ignorar una transición antigua tras cambiar de modo rápidamente.
            if (on) { [gTracker.session pause]; NSLog(@"Traigger: Face ID en pausa; la cara llega por la cámara frontal"); }
            else { TraiggerFaceResumeRequested(); NSLog(@"Traigger: solicitada recuperación de ARKit si estaba activo"); }
        };
        if (NSThread.isMainThread) transition(); else dispatch_async(dispatch_get_main_queue(), transition);
    }
    int TraiggerFaceInterrupted(void) {
        [gLock lock]; int value = gInterrupted ? 1 : 0; [gLock unlock];
        return value;
    }
    int TraiggerFaceSupported(void) {
        return ARFaceTrackingConfiguration.isSupported ? 1 : 0;
    }

    int TraiggerFaceStart(void) {
        if (!ARFaceTrackingConfiguration.isSupported) return 0;
        if (!gLock) gLock = [[NSLock alloc] init];
        [gLock lock]; gRequested = true; bool external = gExternal;
        if (!external) { gInterrupted = false; gDenied = false; gErrorCode = 0; gErrorText = @""; gHasNew = false; gCameraFrameAt = 0; gStartedAt = NSProcessInfo.processInfo.systemUptime; }
        [gLock unlock];
        if (external) return 1;
        if (!gTracker) {
            gTracker = [[TraiggerFaceTracker alloc] init];
            gTracker.session = [[ARSession alloc] init];
            gTracker.session.delegate = gTracker;
        }
        ARFaceTrackingConfiguration *configuration = [[ARFaceTrackingConfiguration alloc] init];
        configuration.lightEstimationEnabled = NO;
        // El vídeo no se muestra: basta con la resolución más baja disponible.
        ARVideoFormat *lowest = nil;
        for (ARVideoFormat *format in ARFaceTrackingConfiguration.supportedVideoFormats) {
            if (!lowest || format.imageResolution.width < lowest.imageResolution.width) lowest = format;
        }
        if (lowest) configuration.videoFormat = lowest;
        [gTracker.session runWithConfiguration:configuration options:ARSessionRunOptionResetTracking | ARSessionRunOptionRemoveExistingAnchors];
        return 1;
    }

    void TraiggerFaceStop(void) {
        [gLock lock]; gRequested = false; gHasNew = false; [gLock unlock];
        if (gTracker) [gTracker.session pause];
    }

    // Recuperar sólo si Unity sigue solicitando cara; nunca activar una cámara deshabilitada por el usuario.
    int TraiggerFaceResumeRequested(void) {
        [gLock lock]; bool resume = gRequested && !gExternal; [gLock unlock];
        return resume ? TraiggerFaceStart() : 0;
    }
    // Fotogramas de cámara, no detecciones de cara: mirar fuera de encuadre no se confunde con un conflicto.
    int TraiggerFaceCaptureConflict(void) {
        [gLock lock]; double now = NSProcessInfo.processInfo.systemUptime;
        bool conflict = gRequested && !gExternal && (gProbeFailed || gInterrupted || gErrorCode != 0 || now - (gCameraFrameAt > 0 ? gCameraFrameAt : gStartedAt) > 2.5);
        [gLock unlock]; return conflict ? 1 : 0;
    }
    void TraiggerFaceVisionPriority(void) {
        [gLock lock]; gCapturePriority = false; gHybrid = false; [gLock unlock];
        TraiggerFaceSetExternal(1);
    }
    void TraiggerFaceHybridPriority(void) {
        if (!gLock) gLock = [[NSLock alloc] init];
        [gLock lock]; gCapturePriority = false; gHybrid = true; [gLock unlock];
        TraiggerFaceSetExternal(1);
    }
    void TraiggerFaceCapturePriority(int on) {
        if (!gLock) gLock = [[NSLock alloc] init];
        [gLock lock]; bool wasExternal = gExternal; gCapturePriority = on != 0; gHybrid = false; [gLock unlock];
        if (on || wasExternal) TraiggerFaceSetExternal(on != 0 ? 1 : 0);
    }
    void TraiggerFaceReleaseCapture(void) {
        [gLock lock]; bool wasExternal = gExternal; [gLock unlock];
        TraiggerFaceCapturePriority(0);
        if (!wasExternal) TraiggerFaceResumeRequested();
    }
    void TraiggerFacePreferLocal(void) {
        [gLock lock]; bool wasExternal = gExternal; [gLock unlock];
        if (wasExternal) TraiggerFaceSetExternal(0);
    }
    // Una pausa de segundo plano no acredita conflicto entre cámaras. Dar tiempo al retorno sin reiniciar ARKit.
    void TraiggerFaceForegroundGrace(void) {
        [gLock lock];
        if (gErrorCode == 0) { gProbeFailed = false; gCameraFrameAt = 0; gStartedAt = NSProcessInfo.processInfo.systemUptime; }
        [gLock unlock];
    }
    void TraiggerFaceLocalProbe(int active) {
        [gLock lock]; gLocalProbe = active != 0; if (active) gProbeFailed = false; [gLock unlock];
    }
    int TraiggerFaceSourceMode(void) {
        [gLock lock]; int mode = !gRequested ? 0 : gExternal ? (gHybrid ? 4 : gCapturePriority ? 3 : 2) : 1; [gLock unlock]; return mode;
    }
    void TraiggerFaceDiagnostic(char *buffer, int count) {
        if (!buffer || count < 1) return;
        [gLock lock];
        double age = gCameraFrameAt > 0 ? NSProcessInfo.processInfo.systemUptime - gCameraFrameAt : -1;
        NSString *text = [NSString stringWithFormat:@"%@ · cámara hace %.1f s · %lu fotogramas · interrupción %@ · error %ld %@", !gRequested ? @"Detenido" : gExternal ? (gHybrid ? @"MediaPipe híbrido" : gCapturePriority ? @"ARKit en pausa: prioridad vídeo USB" : @"Vision aproximado") : @"ARKit completo", age, gFrameCount, gInterrupted ? @"sí" : @"no", (long)gErrorCode, gErrorText];
        snprintf(buffer, count, "%s", text.UTF8String); [gLock unlock];
    }

    // Devuelve 1 si hay datos nuevos (copiados en values), 0 si no y -1 si se denegó el permiso de cámara.
    int TraiggerFaceRead(float *values, int count) {
        if (!gLock) return 0;
        int result = 0;
        [gLock lock];
        if (gDenied) result = -1;
        else if (gHasNew) {
            memcpy(values, gValues, sizeof(float) * (count < kValueCount ? count : kValueCount));
            gHasNew = false;
            result = 1;
        }
        [gLock unlock];
        return result;
    }
}
