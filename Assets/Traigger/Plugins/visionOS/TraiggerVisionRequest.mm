// Unity → host nativo de Vision Pro: peticiones sencillas ("sensors:on", "sensors:off") por NotificationCenter.
#import <Foundation/Foundation.h>
extern "C" void TraiggerVisionRequest(const char *request) {
    NSString *value = [NSString stringWithUTF8String:request ? request : ""];
    dispatch_async(dispatch_get_main_queue(), ^{
        [[NSNotificationCenter defaultCenter] postNotificationName:@"Traigger.Request" object:nil userInfo:@{@"request": value}];
    });
}
