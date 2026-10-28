// «Traigger»: dispositivo de audio virtual en bucle para macOS (AudioServerPlugIn de Core Audio).
// Lo que una app reproduce en su salida aparece en su entrada. Traigger Camera reproduce aquí la mezcla de tu voz y
// el sonido de la película, y FaceTime (o Zoom, Meet…) usa «Traigger» como micrófono.
// Un dispositivo, una entrada y una salida, estéreo, 48 kHz, float32. Se instala en /Library/Audio/Plug-Ins/HAL.
#include <CoreAudio/AudioServerPlugIn.h>
#include <mach/mach_time.h>
#include <pthread.h>
#include <stdatomic.h>
#include <string.h>

enum { kPlugIn = kAudioObjectPlugInObject, kDevice = 2, kStreamIn = 3, kStreamOut = 4 };
#define kRate 48000.0
#define kChannels 2
#define kRing 16384
#define kDeviceUID "TraiggerAudio_UID"
#define kModelUID "TraiggerAudio_Model"

static AudioServerPlugInHostRef host;
static pthread_mutex_t lock = PTHREAD_MUTEX_INITIALIZER;
static UInt32 refCount;
static UInt32 ioClients;
static Float64 ticksPerFrame;
static UInt64 anchorHostTime, timestampCount;
static float ring[kRing * kChannels];

static AudioServerPlugInDriverInterface interfaceTable;
static AudioServerPlugInDriverInterface *interfacePointer = &interfaceTable;
static AudioServerPlugInDriverRef driverRef = &interfacePointer;

static AudioStreamBasicDescription Format(void) {
    AudioStreamBasicDescription f = {0};
    f.mSampleRate = kRate; f.mFormatID = kAudioFormatLinearPCM;
    f.mFormatFlags = kAudioFormatFlagIsFloat | kAudioFormatFlagsNativeEndian | kAudioFormatFlagIsPacked;
    f.mBytesPerPacket = f.mBytesPerFrame = sizeof(float) * kChannels; f.mFramesPerPacket = 1;
    f.mChannelsPerFrame = kChannels; f.mBitsPerChannel = 32;
    return f;
}

// MARK: COM
static HRESULT QueryInterface(void *self, REFIID iid, LPVOID *out) {
    CFUUIDRef requested = CFUUIDCreateFromUUIDBytes(NULL, iid);
    HRESULT result = E_NOINTERFACE;
    if (CFEqual(requested, IUnknownUUID) || CFEqual(requested, kAudioServerPlugInDriverInterfaceUUID)) {
        pthread_mutex_lock(&lock); refCount++; pthread_mutex_unlock(&lock);
        *out = driverRef; result = S_OK;
    }
    CFRelease(requested);
    return result;
}
static ULONG AddRef(void *self) { pthread_mutex_lock(&lock); ULONG r = ++refCount; pthread_mutex_unlock(&lock); return r; }
static ULONG Release(void *self) { pthread_mutex_lock(&lock); if (refCount > 0) refCount--; ULONG r = refCount; pthread_mutex_unlock(&lock); return r; }

static OSStatus Initialize(AudioServerPlugInDriverRef d, AudioServerPlugInHostRef h) {
    host = h;
    mach_timebase_info_data_t info; mach_timebase_info(&info);
    ticksPerFrame = (1000000000.0 * info.denom / info.numer) / kRate;
    return 0;
}
static OSStatus CreateDevice(AudioServerPlugInDriverRef d, CFDictionaryRef desc, const AudioServerPlugInClientInfo *c, AudioObjectID *o) { return kAudioHardwareUnsupportedOperationError; }
static OSStatus DestroyDevice(AudioServerPlugInDriverRef d, AudioObjectID o) { return kAudioHardwareUnsupportedOperationError; }
static OSStatus AddDeviceClient(AudioServerPlugInDriverRef d, AudioObjectID o, const AudioServerPlugInClientInfo *c) { return 0; }
static OSStatus RemoveDeviceClient(AudioServerPlugInDriverRef d, AudioObjectID o, const AudioServerPlugInClientInfo *c) { return 0; }
static OSStatus PerformConfigChange(AudioServerPlugInDriverRef d, AudioObjectID o, UInt64 a, void *i) { return 0; }
static OSStatus AbortConfigChange(AudioServerPlugInDriverRef d, AudioObjectID o, UInt64 a, void *i) { return 0; }

// MARK: Propiedades
static Boolean HasProperty(AudioServerPlugInDriverRef d, AudioObjectID o, pid_t p, const AudioObjectPropertyAddress *a) {
    switch (a->mSelector) {
        case kAudioObjectPropertyBaseClass: case kAudioObjectPropertyClass: case kAudioObjectPropertyOwner:
        case kAudioObjectPropertyOwnedObjects: case kAudioObjectPropertyName: case kAudioObjectPropertyManufacturer:
            return true;
    }
    if (o == kPlugIn) switch (a->mSelector) {
        case kAudioPlugInPropertyDeviceList: case kAudioPlugInPropertyTranslateUIDToDevice: case kAudioPlugInPropertyResourceBundle: return true;
    }
    if (o == kDevice) switch (a->mSelector) {
        case kAudioDevicePropertyDeviceUID: case kAudioDevicePropertyModelUID: case kAudioDevicePropertyTransportType:
        case kAudioDevicePropertyRelatedDevices: case kAudioDevicePropertyClockDomain: case kAudioDevicePropertyDeviceIsAlive:
        case kAudioDevicePropertyDeviceIsRunning: case kAudioDevicePropertyDeviceCanBeDefaultDevice:
        case kAudioDevicePropertyDeviceCanBeDefaultSystemDevice: case kAudioDevicePropertyLatency: case kAudioDevicePropertyStreams:
        case kAudioObjectPropertyControlList: case kAudioDevicePropertySafetyOffset: case kAudioDevicePropertyNominalSampleRate:
        case kAudioDevicePropertyAvailableNominalSampleRates: case kAudioDevicePropertyIsHidden: case kAudioDevicePropertyZeroTimeStampPeriod:
        case kAudioDevicePropertyPreferredChannelsForStereo:
            return true;
    }
    if (o == kStreamIn || o == kStreamOut) switch (a->mSelector) {
        case kAudioStreamPropertyIsActive: case kAudioStreamPropertyDirection: case kAudioStreamPropertyTerminalType:
        case kAudioStreamPropertyStartingChannel: case kAudioStreamPropertyLatency: case kAudioStreamPropertyVirtualFormat:
        case kAudioStreamPropertyPhysicalFormat: case kAudioStreamPropertyAvailableVirtualFormats: case kAudioStreamPropertyAvailablePhysicalFormats:
            return true;
    }
    return false;
}

static OSStatus IsPropertySettable(AudioServerPlugInDriverRef d, AudioObjectID o, pid_t p, const AudioObjectPropertyAddress *a, Boolean *out) {
    if (!HasProperty(d, o, p, a)) return kAudioHardwareUnknownPropertyError;
    *out = (a->mSelector == kAudioDevicePropertyNominalSampleRate || a->mSelector == kAudioStreamPropertyVirtualFormat ||
            a->mSelector == kAudioStreamPropertyPhysicalFormat || a->mSelector == kAudioStreamPropertyIsActive);
    return 0;
}

static UInt32 StreamsFor(AudioObjectPropertyScope scope, AudioObjectID *list) {
    UInt32 n = 0;
    if (scope == kAudioObjectPropertyScopeGlobal || scope == kAudioObjectPropertyScopeInput) list[n++] = kStreamIn;
    if (scope == kAudioObjectPropertyScopeGlobal || scope == kAudioObjectPropertyScopeOutput) list[n++] = kStreamOut;
    return n;
}

static OSStatus GetPropertyData(AudioServerPlugInDriverRef d, AudioObjectID o, pid_t p, const AudioObjectPropertyAddress *a,
                                UInt32 qs, const void *q, UInt32 size, UInt32 *used, void *out);

static OSStatus GetPropertyDataSize(AudioServerPlugInDriverRef d, AudioObjectID o, pid_t p, const AudioObjectPropertyAddress *a,
                                    UInt32 qs, const void *q, UInt32 *out) {
    switch (a->mSelector) {
        case kAudioObjectPropertyName: case kAudioObjectPropertyManufacturer: case kAudioDevicePropertyDeviceUID:
        case kAudioDevicePropertyModelUID: case kAudioPlugInPropertyResourceBundle: *out = sizeof(CFStringRef); return 0;
        case kAudioObjectPropertyBaseClass: case kAudioObjectPropertyClass: *out = sizeof(AudioClassID); return 0;
        case kAudioObjectPropertyOwner: case kAudioPlugInPropertyTranslateUIDToDevice: *out = sizeof(AudioObjectID); return 0;
        case kAudioObjectPropertyControlList: *out = 0; return 0;
        case kAudioDevicePropertyNominalSampleRate: *out = sizeof(Float64); return 0;
        case kAudioDevicePropertyAvailableNominalSampleRates: *out = sizeof(AudioValueRange); return 0;
        case kAudioDevicePropertyPreferredChannelsForStereo: *out = 2 * sizeof(UInt32); return 0;
        case kAudioStreamPropertyVirtualFormat: case kAudioStreamPropertyPhysicalFormat: *out = sizeof(AudioStreamBasicDescription); return 0;
        case kAudioStreamPropertyAvailableVirtualFormats: case kAudioStreamPropertyAvailablePhysicalFormats: *out = sizeof(AudioStreamRangedDescription); return 0;
        case kAudioObjectPropertyOwnedObjects:
            *out = (o == kPlugIn ? 1 : o == kDevice ? StreamsFor(a->mScope, (AudioObjectID[2]){0}) : 0) * sizeof(AudioObjectID); return 0;
        case kAudioPlugInPropertyDeviceList: case kAudioDevicePropertyRelatedDevices: *out = sizeof(AudioObjectID); return 0;
        case kAudioDevicePropertyStreams: { AudioObjectID l[2]; *out = StreamsFor(a->mScope, l) * sizeof(AudioObjectID); return 0; }
        default: *out = sizeof(UInt32); return HasProperty(d, o, p, a) ? 0 : kAudioHardwareUnknownPropertyError;
    }
}

#define PUT(type, value) do { if (size < sizeof(type)) return kAudioHardwareBadPropertySizeError; *(type *)out = (value); *used = sizeof(type); return 0; } while (0)
static OSStatus GetPropertyData(AudioServerPlugInDriverRef d, AudioObjectID o, pid_t p, const AudioObjectPropertyAddress *a,
                                UInt32 qs, const void *q, UInt32 size, UInt32 *used, void *out) {
    switch (a->mSelector) {
        case kAudioObjectPropertyBaseClass: PUT(AudioClassID, kAudioObjectClassID);
        case kAudioObjectPropertyClass: PUT(AudioClassID, o == kPlugIn ? kAudioPlugInClassID : o == kDevice ? kAudioDeviceClassID : kAudioStreamClassID);
        case kAudioObjectPropertyOwner: PUT(AudioObjectID, o == kPlugIn ? kAudioObjectUnknown : o == kDevice ? kPlugIn : kDevice);
        case kAudioObjectPropertyName: PUT(CFStringRef, CFSTR("Traigger"));
        case kAudioObjectPropertyManufacturer: PUT(CFStringRef, CFSTR("Traigger"));
        case kAudioPlugInPropertyResourceBundle: PUT(CFStringRef, CFSTR(""));
        case kAudioDevicePropertyDeviceUID: PUT(CFStringRef, CFSTR(kDeviceUID));
        case kAudioDevicePropertyModelUID: PUT(CFStringRef, CFSTR(kModelUID));
        case kAudioPlugInPropertyTranslateUIDToDevice: {
            CFStringRef uid = qs == sizeof(CFStringRef) ? *(const CFStringRef *)q : NULL;
            PUT(AudioObjectID, uid && CFStringCompare(uid, CFSTR(kDeviceUID), 0) == kCFCompareEqualTo ? kDevice : kAudioObjectUnknown);
        }
        case kAudioObjectPropertyOwnedObjects: {
            AudioObjectID list[2]; UInt32 n = o == kPlugIn ? (list[0] = kDevice, 1) : o == kDevice ? StreamsFor(a->mScope, list) : 0;
            n = n < size / sizeof(AudioObjectID) ? n : size / sizeof(AudioObjectID);
            memcpy(out, list, n * sizeof(AudioObjectID)); *used = n * sizeof(AudioObjectID); return 0;
        }
        case kAudioPlugInPropertyDeviceList: case kAudioDevicePropertyRelatedDevices: PUT(AudioObjectID, kDevice);
        case kAudioDevicePropertyStreams: {
            AudioObjectID list[2]; UInt32 n = StreamsFor(a->mScope, list);
            n = n < size / sizeof(AudioObjectID) ? n : size / sizeof(AudioObjectID);
            memcpy(out, list, n * sizeof(AudioObjectID)); *used = n * sizeof(AudioObjectID); return 0;
        }
        case kAudioObjectPropertyControlList: *used = 0; return 0;
        case kAudioDevicePropertyTransportType: PUT(UInt32, kAudioDeviceTransportTypeVirtual);
        case kAudioDevicePropertyClockDomain: PUT(UInt32, 0);
        case kAudioDevicePropertyDeviceIsAlive: PUT(UInt32, 1);
        case kAudioDevicePropertyDeviceIsRunning: { pthread_mutex_lock(&lock); UInt32 r = ioClients > 0; pthread_mutex_unlock(&lock); PUT(UInt32, r); }
        case kAudioDevicePropertyDeviceCanBeDefaultDevice: PUT(UInt32, 1);
        case kAudioDevicePropertyDeviceCanBeDefaultSystemDevice: PUT(UInt32, 0);
        case kAudioDevicePropertyLatency: case kAudioDevicePropertySafetyOffset: PUT(UInt32, 0); // también kAudioStreamPropertyLatency (mismo código)
        case kAudioDevicePropertyNominalSampleRate: PUT(Float64, kRate);
        case kAudioDevicePropertyAvailableNominalSampleRates: PUT(AudioValueRange, ((AudioValueRange){kRate, kRate}));
        case kAudioDevicePropertyIsHidden: PUT(UInt32, 0);
        case kAudioDevicePropertyZeroTimeStampPeriod: PUT(UInt32, kRing);
        case kAudioDevicePropertyPreferredChannelsForStereo:
            if (size < 2 * sizeof(UInt32)) return kAudioHardwareBadPropertySizeError;
            ((UInt32 *)out)[0] = 1; ((UInt32 *)out)[1] = 2; *used = 2 * sizeof(UInt32); return 0;
        case kAudioStreamPropertyIsActive: PUT(UInt32, 1);
        case kAudioStreamPropertyDirection: PUT(UInt32, o == kStreamIn ? 1 : 0);
        case kAudioStreamPropertyTerminalType: PUT(UInt32, o == kStreamIn ? kAudioStreamTerminalTypeMicrophone : kAudioStreamTerminalTypeSpeaker);
        case kAudioStreamPropertyStartingChannel: PUT(UInt32, 1);
        case kAudioStreamPropertyVirtualFormat: case kAudioStreamPropertyPhysicalFormat: PUT(AudioStreamBasicDescription, Format());
        case kAudioStreamPropertyAvailableVirtualFormats: case kAudioStreamPropertyAvailablePhysicalFormats: {
            AudioStreamRangedDescription r; r.mFormat = Format(); r.mSampleRateRange.mMinimum = r.mSampleRateRange.mMaximum = kRate;
            PUT(AudioStreamRangedDescription, r);
        }
    }
    return kAudioHardwareUnknownPropertyError;
}

// Formato fijo: se aceptan las escrituras que piden lo mismo que ya hay.
static OSStatus SetPropertyData(AudioServerPlugInDriverRef d, AudioObjectID o, pid_t p, const AudioObjectPropertyAddress *a,
                                UInt32 qs, const void *q, UInt32 size, const void *data) {
    switch (a->mSelector) {
        case kAudioDevicePropertyNominalSampleRate: return size == sizeof(Float64) && *(const Float64 *)data == kRate ? 0 : kAudioHardwareIllegalOperationError;
        case kAudioStreamPropertyVirtualFormat: case kAudioStreamPropertyPhysicalFormat:
            return size == sizeof(AudioStreamBasicDescription) && ((const AudioStreamBasicDescription *)data)->mSampleRate == kRate ? 0 : kAudioDeviceUnsupportedFormatError;
        case kAudioStreamPropertyIsActive: return 0;
    }
    return kAudioHardwareUnknownPropertyError;
}

// MARK: E/S
static OSStatus StartIO(AudioServerPlugInDriverRef d, AudioObjectID o, UInt32 client) {
    pthread_mutex_lock(&lock);
    if (ioClients++ == 0) { anchorHostTime = mach_absolute_time(); timestampCount = 0; memset(ring, 0, sizeof ring); }
    pthread_mutex_unlock(&lock);
    return 0;
}
static OSStatus StopIO(AudioServerPlugInDriverRef d, AudioObjectID o, UInt32 client) {
    pthread_mutex_lock(&lock); if (ioClients > 0) ioClients--; pthread_mutex_unlock(&lock);
    return 0;
}
static OSStatus GetZeroTimeStamp(AudioServerPlugInDriverRef d, AudioObjectID o, UInt32 client, Float64 *sampleTime, UInt64 *hostTime, UInt64 *seed) {
    pthread_mutex_lock(&lock);
    Float64 ticksPerRing = ticksPerFrame * kRing;
    UInt64 now = mach_absolute_time();
    if (anchorHostTime + (UInt64)((timestampCount + 1) * ticksPerRing) <= now) timestampCount++;
    *sampleTime = (Float64)(timestampCount * kRing);
    *hostTime = anchorHostTime + (UInt64)(timestampCount * ticksPerRing);
    *seed = 1;
    pthread_mutex_unlock(&lock);
    return 0;
}
static OSStatus WillDoIO(AudioServerPlugInDriverRef d, AudioObjectID o, UInt32 client, UInt32 op, Boolean *will, Boolean *inPlace) {
    *will = op == kAudioServerPlugInIOOperationReadInput || op == kAudioServerPlugInIOOperationWriteMix; *inPlace = true;
    return 0;
}
static OSStatus BeginIO(AudioServerPlugInDriverRef d, AudioObjectID o, UInt32 client, UInt32 op, UInt32 frames, const AudioServerPlugInIOCycleInfo *info) { return 0; }
static OSStatus EndIO(AudioServerPlugInDriverRef d, AudioObjectID o, UInt32 client, UInt32 op, UInt32 frames, const AudioServerPlugInIOCycleInfo *info) { return 0; }

// El bucle: la salida se escribe en el anillo según su instante de muestra y la entrada lee ese mismo instante.
static OSStatus DoIO(AudioServerPlugInDriverRef d, AudioObjectID o, AudioObjectID stream, UInt32 client, UInt32 op, UInt32 frames,
                     const AudioServerPlugInIOCycleInfo *info, void *main, void *secondary) {
    float *buffer = main;
    if (op == kAudioServerPlugInIOOperationWriteMix) {
        UInt64 start = (UInt64)info->mOutputTime.mSampleTime;
        for (UInt32 i = 0; i < frames; i++) {
            UInt64 at = ((start + i) % kRing) * kChannels;
            for (UInt32 c = 0; c < kChannels; c++) ring[at + c] = buffer[i * kChannels + c];
        }
    } else if (op == kAudioServerPlugInIOOperationReadInput) {
        UInt64 start = (UInt64)info->mInputTime.mSampleTime;
        for (UInt32 i = 0; i < frames; i++) {
            UInt64 at = ((start + i) % kRing) * kChannels;
            for (UInt32 c = 0; c < kChannels; c++) { buffer[i * kChannels + c] = ring[at + c]; ring[at + c] = 0; }
        }
    }
    return 0;
}

// MARK: Fábrica
void *TraiggerAudio_Create(CFAllocatorRef allocator, CFUUIDRef type) {
    if (!CFEqual(type, kAudioServerPlugInTypeUUID)) return NULL;
    interfaceTable = (AudioServerPlugInDriverInterface){
        NULL, QueryInterface, AddRef, Release, Initialize, CreateDevice, DestroyDevice, AddDeviceClient, RemoveDeviceClient,
        PerformConfigChange, AbortConfigChange, HasProperty, IsPropertySettable, GetPropertyDataSize, GetPropertyData, SetPropertyData,
        StartIO, StopIO, GetZeroTimeStamp, WillDoIO, BeginIO, DoIO, EndIO };
    return driverRef;
}
