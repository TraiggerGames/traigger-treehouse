#import <Foundation/Foundation.h>
#include <string.h>

// Almacén clave-valor de iCloud para el perfil de voz (VoiceProfileStore.cs). Necesita el permiso
// com.apple.developer.ubiquity-kvstore-identifier, que añaden las compilaciones si la sincronización está activada.
extern "C" {
bool TraiggerCloudAvailable(void) {
    return [NSFileManager defaultManager].ubiquityIdentityToken != nil;
}
void TraiggerCloudSet(const char *key, const char *value) {
    NSUbiquitousKeyValueStore *store = [NSUbiquitousKeyValueStore defaultStore];
    [store setString:[NSString stringWithUTF8String:value] forKey:[NSString stringWithUTF8String:key]];
    [store synchronize];
}
char *TraiggerCloudGet(const char *key) {
    NSUbiquitousKeyValueStore *store = [NSUbiquitousKeyValueStore defaultStore];
    [store synchronize];
    NSString *value = [store stringForKey:[NSString stringWithUTF8String:key]];
    return value ? strdup(value.UTF8String) : NULL;
}

// Imágenes y vídeos de escenas y carteles en iCloud Drive (StudioCloud.cs). Necesita el contenedor de iCloud de la
// app (com.apple.developer.icloud-container-identifiers), que también añaden las compilaciones con iCloud activado.
static NSURL *TraiggerCloudMediaFolder(void) {
    static NSURL *folder;
    static dispatch_once_t once;
    dispatch_once(&once, ^{
        NSURL *root = [[NSFileManager defaultManager] URLForUbiquityContainerIdentifier:nil];   // lento la primera vez
        if (!root) return;
        folder = [[root URLByAppendingPathComponent:@"Documents"] URLByAppendingPathComponent:@"Medios"];
        [[NSFileManager defaultManager] createDirectoryAtURL:folder withIntermediateDirectories:YES attributes:nil error:nil];
    });
    return folder;
}
// Copia un archivo del aparato a iCloud Drive y devuelve «icloud://nombre» (el nombre lleva el tamaño y la fecha
// para no pisar otro archivo con el mismo nombre). Si ya es un archivo de iCloud, devuelve su nombre.
char *TraiggerCloudMediaPut(const char *path) {
    NSURL *folder = TraiggerCloudMediaFolder();
    if (!folder) return NULL;
    NSString *source = [NSString stringWithUTF8String:path];
    if ([source hasPrefix:folder.path]) return strdup([@"icloud://" stringByAppendingString:source.lastPathComponent].UTF8String);
    NSDictionary *info = [[NSFileManager defaultManager] attributesOfItemAtPath:source error:nil];
    if (!info) return NULL;
    NSString *name = [NSString stringWithFormat:@"%llu-%.0f-%@", [info fileSize], [[info fileModificationDate] timeIntervalSince1970], source.lastPathComponent];
    NSURL *target = [folder URLByAppendingPathComponent:name];
    if (![[NSFileManager defaultManager] fileExistsAtPath:target.path]) {
        NSError *error = nil;
        if (![[NSFileManager defaultManager] copyItemAtURL:[NSURL fileURLWithPath:source] toURL:target error:&error]) {
            NSLog(@"Traigger: no se pudo subir %@ a iCloud: %@", name, error.localizedDescription);
            return NULL;
        }
    }
    return strdup([@"icloud://" stringByAppendingString:name].UTF8String);
}
// Ruta local de un archivo de iCloud Drive, o NULL si aún no está descargado (pide la descarga).
char *TraiggerCloudMediaGet(const char *name) {
    NSURL *folder = TraiggerCloudMediaFolder();
    if (!folder) return NULL;
    NSURL *url = [folder URLByAppendingPathComponent:[NSString stringWithUTF8String:name]];
    NSString *status = nil;
    [url getResourceValue:&status forKey:NSURLUbiquitousItemDownloadingStatusKey error:nil];
    if ([[NSFileManager defaultManager] fileExistsAtPath:url.path] && (!status || [status isEqualToString:NSURLUbiquitousItemDownloadingStatusCurrent]))
        return strdup(url.path.UTF8String);
    [[NSFileManager defaultManager] startDownloadingUbiquitousItemAtURL:url error:nil];
    return NULL;
}
}
