// Reproductor de películas de Traigger con libVLC 4 (VLCKit 4 en Mac, iPhone/iPad y Vision Pro): abre cualquier
// formato (MKV, subtítulos, varias pistas) desde una carpeta compartida SMB o un archivo local, y deja cada fotograma
// en memoria (BGRA, hasta 1080p) para que Unity lo ponga de fondo detrás del personaje (MoviePlayer.cs).
// El sonido no lo abre VLC: llega en memoria y lo reproduce un motor de audio propio (ver «Sonido» más abajo). Se compila en el proyecto de iPhone (Unity) y en un .bundle para el Mac
// (Plugins/macOS, ver MacCamera/MoviePlugin/build.sh).
#import <AVFoundation/AVFoundation.h>
#import <QuartzCore/QuartzCore.h>
#include <pthread.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

// Lo justo de la API en C de libVLC 4 (VLCKit 4, vlc/vlc.h), declarado aquí con sus firmas exactas para no depender
// de rutas de cabeceras en los proyectos de Xcode de Mac, iPhone/iPad y Vision Pro.
#include <stdbool.h>
typedef struct libvlc_instance_t libvlc_instance_t;
typedef struct libvlc_media_t libvlc_media_t;
typedef struct libvlc_media_player_t libvlc_media_player_t;
typedef struct libvlc_media_list_t libvlc_media_list_t;
typedef struct libvlc_media_tracklist_t libvlc_media_tracklist_t;
typedef int64_t libvlc_time_t;
typedef enum { libvlc_track_unknown = -1, libvlc_track_audio = 0, libvlc_track_video = 1, libvlc_track_text = 2 } libvlc_track_type_t;
typedef struct libvlc_media_track_t {
    uint32_t i_codec, i_original_fourcc;
    int i_id;
    libvlc_track_type_t i_type;
    int i_profile, i_level;
    union { void *audio, *video, *subtitle; };
    unsigned int i_bitrate;
    char *psz_language, *psz_description;
    const char *psz_id;
    bool id_stable;
    char *psz_name;
    bool selected;
} libvlc_media_track_t;
extern libvlc_instance_t *libvlc_new(int argc, const char *const *argv);
extern libvlc_media_t *libvlc_media_new_location(const char *);
extern libvlc_media_t *libvlc_media_new_path(const char *);
extern void libvlc_media_release(libvlc_media_t *);
extern int libvlc_media_parse_request(libvlc_instance_t *, libvlc_media_t *, int flags, int timeout);
extern int libvlc_media_get_parsed_status(libvlc_media_t *);
extern libvlc_media_list_t *libvlc_media_subitems(libvlc_media_t *);
extern int libvlc_media_list_count(libvlc_media_list_t *);
extern libvlc_media_t *libvlc_media_list_item_at_index(libvlc_media_list_t *, int);
extern void libvlc_media_list_lock(libvlc_media_list_t *);
extern void libvlc_media_list_unlock(libvlc_media_list_t *);
extern void libvlc_media_list_release(libvlc_media_list_t *);
extern char *libvlc_media_get_mrl(libvlc_media_t *);
extern char *libvlc_media_get_meta(libvlc_media_t *, int meta);
extern int libvlc_media_get_type(libvlc_media_t *);
extern void libvlc_free(void *);
extern libvlc_media_player_t *libvlc_media_player_new(libvlc_instance_t *);
extern void libvlc_media_player_set_media(libvlc_media_player_t *, libvlc_media_t *);
extern int libvlc_media_player_play(libvlc_media_player_t *);
extern void libvlc_media_player_set_pause(libvlc_media_player_t *, int);
extern int libvlc_media_player_stop_async(libvlc_media_player_t *);
extern bool libvlc_media_player_is_playing(libvlc_media_player_t *);
extern libvlc_time_t libvlc_media_player_get_time(libvlc_media_player_t *);
extern libvlc_time_t libvlc_media_player_get_length(libvlc_media_player_t *);
extern int libvlc_media_player_set_time(libvlc_media_player_t *, libvlc_time_t, bool fast);
extern int libvlc_audio_set_volume(libvlc_media_player_t *, int);
extern int libvlc_audio_set_delay(libvlc_media_player_t *, int64_t);
extern int libvlc_audio_get_volume(libvlc_media_player_t *);
extern void libvlc_audio_set_mute(libvlc_media_player_t *, int);
extern int libvlc_audio_get_mute(libvlc_media_player_t *);
typedef unsigned (*libvlc_video_format_cb)(void **, char *, unsigned *, unsigned *, unsigned *, unsigned *);
typedef void (*libvlc_video_cleanup_cb)(void *);
typedef void *(*libvlc_video_lock_cb)(void *, void **);
typedef void (*libvlc_video_unlock_cb)(void *, void *, void *const *);
typedef void (*libvlc_video_display_cb)(void *, void *);
extern void libvlc_video_set_format_callbacks(libvlc_media_player_t *, libvlc_video_format_cb, libvlc_video_cleanup_cb);
extern void libvlc_video_set_callbacks(libvlc_media_player_t *, libvlc_video_lock_cb, libvlc_video_unlock_cb, libvlc_video_display_cb, void *);
extern libvlc_media_tracklist_t *libvlc_media_player_get_tracklist(libvlc_media_player_t *, libvlc_track_type_t, bool selected);
extern size_t libvlc_media_tracklist_count(const libvlc_media_tracklist_t *);
extern libvlc_media_track_t *libvlc_media_tracklist_at(libvlc_media_tracklist_t *, size_t);
extern void libvlc_media_tracklist_delete(libvlc_media_tracklist_t *);
extern void libvlc_media_player_select_track(libvlc_media_player_t *, const libvlc_media_track_t *);
extern void libvlc_media_player_unselect_track_type(libvlc_media_player_t *, libvlc_track_type_t);
extern int libvlc_media_player_add_slave(libvlc_media_player_t *, int type, const char *uri, bool select);
extern void libvlc_media_add_option(libvlc_media_t *, const char *);
extern void libvlc_media_player_release(libvlc_media_player_t *);
extern void libvlc_audio_set_format(libvlc_media_player_t *, const char *format, unsigned rate, unsigned channels);
typedef void (*libvlc_audio_play_cb)(void *, const void *, unsigned, int64_t);
typedef void (*libvlc_audio_pause_cb)(void *, int64_t);
typedef void (*libvlc_audio_resume_cb)(void *, int64_t);
typedef void (*libvlc_audio_flush_cb)(void *, int64_t);
typedef void (*libvlc_audio_drain_cb)(void *);
extern void libvlc_audio_set_callbacks(libvlc_media_player_t *, libvlc_audio_play_cb, libvlc_audio_pause_cb, libvlc_audio_resume_cb,
                                       libvlc_audio_flush_cb, libvlc_audio_drain_cb, void *);
enum { kParseLocal = 0x01, kParseNetwork = 0x02, kParseFailed = 3, kParseTimeout = 4, kParseCancelled = 5, kParseDone = 6, kTypeDirectory = 2, kMetaTitle = 0, kSlaveSubtitle = 0 };

static libvlc_instance_t *vlc;
static libvlc_media_player_t *player;
static char *currentLocation;

// Sonido de la película hacia el Mac (micrófono «Traigger»): en vez de sonar aquí, se entrega a quien lo envía
// (TraiggerMacCamera.swift registra su función). Se puede cambiar en plena película.
typedef void (*TraiggerAudioSink)(const int16_t *samples, int32_t frames);
static TraiggerAudioSink audioSink;
static int audioToMac, playerAudioMode = -1;
// Todo lo que habla con VLC va en su propia cola: si VLC se atasca (al saltar, al volver a la app con una conexión SMB a
// medio reconectar, al soltar un reproductor), Unity no se queda esperando y la app no se congela. Unity lee la hora,
// la duración y el estado de una copia que esta cola actualiza 5 veces por segundo.
static dispatch_queue_t vlcQueue(void) {
    static dispatch_queue_t queue; static dispatch_once_t once;
    dispatch_once(&once, ^{ queue = dispatch_queue_create("traigger.vlc", dispatch_queue_attr_make_with_qos_class(DISPATCH_QUEUE_SERIAL, QOS_CLASS_USER_INITIATED, 0)); });
    return queue;
}
static _Atomic double cachedTime, cachedLength;
static _Atomic int cachedPlaying;
static double seekHoldUntil;   // tras un salto, la hora pedida manda hasta que VLC la alcance
static void refreshState(void);
void TraiggerMovieSetAudioSink(TraiggerAudioSink sink) { audioSink = sink; }
void TraiggerMovieSendAudio(int enabled) { audioToMac = enabled && audioSink; }
// Sonido: VLC nunca abre la salida de audio del sistema. Su salida propia de iOS/visionOS chocaba con la sesión de audio
// que ya tiene el micrófono abierto (la boca de Traigger), y muchas veces la película arrancaba muda, sobre todo en
// Vision Pro, aunque los subtítulos (que reciben el audio en memoria) sí funcionaban. Ahora VLC entrega las muestras
// (16 bits, 48 kHz, estéreo) a un búfer circular que reproduce nuestro AVAudioEngine, que convive con el micrófono y se
// vuelve a arrancar solo si el sistema lo detiene (interrupciones, cambios de salida, segundo plano).
// Búfer sin cerrojos (un productor, VLC; un consumidor, el hilo de audio): el hilo de audio nunca espera. Se mantiene un
// colchón de ~0,1 s. Si falta sonido no se rellena a golpes de silencio (eso sonaba como la aguja de un vinilo): se calla
// con un fundido y vuelve con otro cuando el colchón se ha recuperado. Si sobra, se corrige poco a poco (una muestra de
// cada 64) en vez de saltar de golpe.
#include <stdatomic.h>
// Colchón de 0,25 s (VLC adelanta el sonido lo mismo con libvlc_audio_set_delay, así sigue sincronizado con la imagen).
// Mantenerlo siempre cerca de 0,25 s: si se aleja más de 25 ms se corrige quitando o repitiendo una muestra de cada 128.
enum { kRingFrames = 48000 * 2, kTarget = 12000, kResume = 12000, kStarve = 256, kBand = 1200, kWayTooMuch = 48000 };
static int16_t ring[kRingFrames * 2];
static _Atomic int64_t ringRead, ringWrite;
static atomic_bool ringFlush, ringPaused;
static float audioGain = 1;
static AVAudioEngine *audioEngine;
static AVAudioSourceNode *audioSource;

static void ringClear(void) { atomic_store(&ringFlush, true); }
static void ringPush(const int16_t *samples, unsigned frames) {
    int64_t write = atomic_load_explicit(&ringWrite, memory_order_relaxed), read = atomic_load_explicit(&ringRead, memory_order_acquire);
    if (write - read + frames > kRingFrames) frames = (unsigned)(kRingFrames - (write - read));   // lleno: lo que no cabe se pierde
    for (unsigned i = 0; i < frames; i++) { int64_t at = ((write + i) % kRingFrames) * 2; ring[at] = samples[i * 2]; ring[at + 1] = samples[i * 2 + 1]; }
    atomic_store_explicit(&ringWrite, write + frames, memory_order_release);
}
// Estado del hilo de audio (solo lo toca él).
static bool audioPriming = true;
static float audioFade, audioLastL, audioLastR;
static unsigned audioSkipCounter;

// Que la película suene aunque el interruptor de silencio esté puesto: Unity deja la sesión en «ambient/soloAmbient»
// (la que usan los juegos y respeta el silencio). Si sigue así, se pasa a «playback» mezclando con lo demás. Si el
// micrófono la tiene en «playAndRecord» no se toca (también suena con el silencio).
static void ensurePlaybackSession(void) {
#if TARGET_OS_IOS || TARGET_OS_VISION
    AVAudioSession *session = [AVAudioSession sharedInstance];
    if (![session.category isEqualToString:AVAudioSessionCategoryAmbient] && ![session.category isEqualToString:AVAudioSessionCategorySoloAmbient]) return;
    NSError *error = nil;
    if (![session setCategory:AVAudioSessionCategoryPlayback mode:AVAudioSessionModeMoviePlayback options:AVAudioSessionCategoryOptionMixWithOthers error:&error] || ![session setActive:YES error:&error])
        NSLog(@"Traigger: la película no puede ignorar el modo silencio: %@", error.localizedDescription);
#endif
}
static void applyAudioDelay(void);
static void startAudioEngine(void) {
    ensurePlaybackSession();
    if (audioEngine && audioEngine.isRunning) return;
    if (!audioEngine) {
        audioEngine = [[AVAudioEngine alloc] init];
        AVAudioFormat *format = [[AVAudioFormat alloc] initStandardFormatWithSampleRate:48000 channels:2];
        audioSource = [[AVAudioSourceNode alloc] initWithFormat:format renderBlock:^OSStatus(BOOL *silence, const AudioTimeStamp *time, AVAudioFrameCount frames, AudioBufferList *output) {
            float *left = output->mBuffers[0].mData, *right = output->mNumberBuffers > 1 ? output->mBuffers[1].mData : NULL;
            if (atomic_exchange(&ringFlush, false)) { atomic_store(&ringRead, atomic_load(&ringWrite)); audioPriming = true; }
            int64_t read = atomic_load_explicit(&ringRead, memory_order_relaxed), write = atomic_load_explicit(&ringWrite, memory_order_acquire);
            int64_t buffered = write - read;
            if (buffered > kWayTooMuch) { read = write - kTarget; buffered = kTarget; }   // tras un salto: alcanzar la imagen
            if (audioPriming && buffered >= kResume) audioPriming = false;
            bool frozen = atomic_load_explicit(&ringPaused, memory_order_relaxed);   // en pausa: se guarda el colchón tal cual
            float gain = audioGain / 32768.0f;
            for (AVAudioFrameCount i = 0; i < frames; i++) {
                float l = 0, r = 0;
                bool have = !audioPriming && !frozen && read < write;
                if (have) {
                    int64_t at = (read % kRingFrames) * 2; l = ring[at] * gain; r = ring[at + 1] * gain; read++; audioLastL = l; audioLastR = r;
                    // Fuera de la banda: una muestra de cada 128 se salta (va detrás) o se repite (va delante); inaudible.
                    int64_t fill = write - read;
                    if (fill > kTarget + kBand || fill < kTarget - kBand) {
                        if (++audioSkipCounter % 128 == 0) { if (fill > kTarget) { if (read < write) read++; } else read--; }
                    }
                } else {
                    l = audioLastL; r = audioLastR;   // al quedarse sin sonido, la última muestra se apaga con el fundido
                    if (!audioPriming && !frozen && write - read < kStarve) audioPriming = true;   // esperar a recuperar el colchón
                }
                // Fundidos de ~1,3 ms al callar y al volver: sin chasquidos.
                audioFade += have ? (audioFade < 1 ? 1.0f / 64 : 0) : (audioFade > 0 ? -1.0f / 64 : 0);
                if (audioFade < 0) audioFade = 0; if (audioFade > 1) audioFade = 1;
                left[i] = l * audioFade; if (right) right[i] = r * audioFade;
            }
            atomic_store_explicit(&ringRead, read, memory_order_release);
            return noErr;
        }];
        [audioEngine attachNode:audioSource];
        [audioEngine connect:audioSource to:audioEngine.mainMixerNode format:format];
        // El sistema para el motor si cambia la salida (auriculares, AirPods) o tras una interrupción: se rearranca.
        [[NSNotificationCenter defaultCenter] addObserverForName:AVAudioEngineConfigurationChangeNotification object:audioEngine queue:nil usingBlock:^(NSNotification *note) {
            dispatch_after(dispatch_time(DISPATCH_TIME_NOW, NSEC_PER_SEC / 4), dispatch_get_main_queue(), ^{ startAudioEngine(); applyAudioDelay(); });
        }];
    }
    NSError *error = nil;
    [audioEngine prepare];
    if (![audioEngine startAndReturnError:&error]) NSLog(@"Traigger: no arranca el sonido de la película: %@", error.localizedDescription);
    applyAudioDelay();
}

// Emisión en directo (TraiggerStream.swift): recibe también el sonido de la película para su mezcla.
static TraiggerAudioSink streamSink;
void TraiggerMovieSetStreamSink(TraiggerAudioSink sink) { streamSink = sink; }
static void mainAudioPlay(void *data, const void *samples, unsigned count, int64_t pts) {
    if (streamSink) streamSink((const int16_t *)samples, (int32_t)count);
    // Con «Enviar al Mac» el sonido va solo al micrófono «Traigger» del Mac (como antes); si no, suena aquí.
    if (audioToMac && audioSink) { audioSink((const int16_t *)samples, (int32_t)count); return; }
    ringPush((const int16_t *)samples, count);
}
static void mainAudioNothing(void *data, int64_t pts) {}
static void mainAudioPause(void *data, int64_t pts) { atomic_store(&ringPaused, true); }
static void mainAudioResume(void *data, int64_t pts) { atomic_store(&ringPaused, false); }
static void mainAudioFlush(void *data, int64_t pts) { ringClear(); }
static void mainAudioDrain(void *data) {}
// Triple búfer: VLC escribe en uno, Unity lee otro y el tercero guarda el último completo.
static pthread_mutex_t frameLock = PTHREAD_MUTEX_INITIALIZER;
static uint8_t *buffers[3];
static unsigned frameWidth, frameHeight;
static int writing = -1, latest = -1, reading = -1;
static int64_t serial;

static void ensureVLC(void) {
    if (vlc) return;
    const char *args[] = {"--no-video-title-show", "--no-osd", "--no-snapshot-preview", "--avcodec-hw=any"};
    vlc = libvlc_new(4, args);
    player = libvlc_media_player_new(vlc);
}

// Tamaño de salida: el de la película, reducido si hace falta para caber en 1920×1080 sin deformar.
static unsigned formatCallback(void **opaque, char *chroma, unsigned *width, unsigned *height, unsigned *pitches, unsigned *lines) {
    double scale = 1;
    if (*width > 1920) scale = 1920.0 / *width;
    if (*height * scale > 1080) scale = 1080.0 / *height;
    unsigned w = ((unsigned)(*width * scale)) & ~1u, h = ((unsigned)(*height * scale)) & ~1u;
    memcpy(chroma, "BGRA", 4);
    *width = w; *height = h; *pitches = w * 4; *lines = h;
    pthread_mutex_lock(&frameLock);
    for (int i = 0; i < 3; i++) { free(buffers[i]); buffers[i] = calloc((size_t)w * h, 4); }
    frameWidth = w; frameHeight = h; writing = latest = reading = -1;
    pthread_mutex_unlock(&frameLock);
    return 1;
}
static void cleanupCallback(void *opaque) {
    pthread_mutex_lock(&frameLock);
    for (int i = 0; i < 3; i++) { free(buffers[i]); buffers[i] = NULL; }
    frameWidth = frameHeight = 0; writing = latest = reading = -1;
    pthread_mutex_unlock(&frameLock);
}
static void *lockCallback(void *opaque, void **planes) {
    pthread_mutex_lock(&frameLock);
    int index = 0;
    while (index == latest || index == reading) index++;
    writing = index;
    planes[0] = buffers[index];
    pthread_mutex_unlock(&frameLock);
    return NULL;
}
static void unlockCallback(void *opaque, void *picture, void *const *planes) {}
static void displayCallback(void *opaque, void *picture) {
    pthread_mutex_lock(&frameLock);
    latest = writing; writing = -1; serial++;
    pthread_mutex_unlock(&frameLock);
}

// Abre una película: «smb://usuario:contraseña@servidor/carpeta/peli.mkv», otra URL o una ruta local.
int TraiggerMoviePlay(const char *location) {
    if (!location) return 0;
    char *copy = strdup(location);
    atomic_store(&cachedTime, 0); atomic_store(&cachedLength, 0); atomic_store(&cachedPlaying, 1);
    ringClear();
    dispatch_async(dispatch_get_main_queue(), ^{ startAudioEngine(); });
    dispatch_async(vlcQueue(), ^{
        ensureVLC();
        if (!vlc) { free(copy); return; }
        libvlc_media_t *media = strstr(copy, "://") ? libvlc_media_new_location(copy) : libvlc_media_new_path(copy);
        free(currentLocation); currentLocation = copy;
        if (!media) return;
        // Búfer de lectura: en red (SMB, internet) 3 s por delante para aguantar baches; en el aparato, 1,5 s.
        libvlc_media_add_option(media, ":network-caching=3000");
        libvlc_media_add_option(media, ":file-caching=1500");
        libvlc_media_add_option(media, ":live-caching=1500");
        // Reproductor nuevo para cada película: reutilizar el anterior tras un «stop» asíncrono hacía que, a veces, la
        // parada terminara después de arrancar la nueva y se llevara la salida de sonido (volvía al pausar o saltar).
        if (player) libvlc_media_player_release(player);
        player = libvlc_media_player_new(vlc);
        libvlc_audio_set_callbacks(player, mainAudioPlay, mainAudioPause, mainAudioResume, mainAudioFlush, mainAudioDrain, NULL);
        libvlc_audio_set_format(player, "S16N", 48000, 2);
        // El sonido pasa por nuestro colchón de 0,25 s: VLC lo entrega ese tiempo antes para que no vaya detrás de la imagen.
        atomic_store(&ringPaused, false);
        applyAudioDelay();
        playerAudioMode = audioToMac;
        libvlc_video_set_format_callbacks(player, formatCallback, cleanupCallback);
        libvlc_video_set_callbacks(player, lockCallback, unlockCallback, displayCallback, NULL);
        libvlc_media_player_set_media(player, media);
        libvlc_media_release(media);
        if (libvlc_media_player_play(player) != 0) atomic_store(&cachedPlaying, 0);
        refreshState();
    });
    return 1;
}
// Adelanto del sonido = nuestro colchón + el retardo de la salida (altavoz ~10 ms, AirPods/Bluetooth 150-250 ms).
// Se recalcula al cambiar de salida.
static void applyAudioDelay(void) {
    double seconds = (double)kTarget / 48000;
    if (audioEngine) seconds += audioEngine.outputNode.presentationLatency;
#if TARGET_OS_IOS || TARGET_OS_VISION
    if (!audioEngine || audioEngine.outputNode.presentationLatency <= 0) seconds += [AVAudioSession sharedInstance].outputLatency + [AVAudioSession sharedInstance].IOBufferDuration;
#endif
    dispatch_async(vlcQueue(), ^{ if (player && !playerAudioMode) libvlc_audio_set_delay(player, -(int64_t)(seconds * 1000000)); });
}
void TraiggerMoviePause(int paused) { dispatch_async(vlcQueue(), ^{ if (player) libvlc_media_player_set_pause(player, paused); }); }
void TraiggerMovieStop(void) { atomic_store(&cachedPlaying, 0); dispatch_async(vlcQueue(), ^{ if (player) libvlc_media_player_stop_async(player); }); }
// El volumen solo se puede fijar cuando ya existe la salida de sonido (antes VLC lo ignora): se reaplica mientras suena.
static int wantedVolume = 100;
// El volumen lo aplica nuestro motor (VLC entrega siempre a tope); aquí también se rearranca el motor si se paró.
static void keepVolume(void) {
    audioGain = wantedVolume / 100.0f;
    dispatch_async(dispatch_get_main_queue(), ^{ ensurePlaybackSession(); });
    static atomic_bool pending;
    if (!atomic_exchange(&pending, true)) dispatch_async(vlcQueue(), ^{
        atomic_store(&pending, false);
        if (!player || !libvlc_media_player_is_playing(player)) return;
        int current = libvlc_audio_get_volume(player);
        if (current >= 0 && current != 100) libvlc_audio_set_volume(player, 100);
        if (libvlc_audio_get_mute(player) == 1) libvlc_audio_set_mute(player, 0);
    });
    if (!atomic_load(&cachedPlaying)) return;
    if (!audioToMac && !(audioEngine && audioEngine.isRunning)) dispatch_async(dispatch_get_main_queue(), ^{ startAudioEngine(); });
}
// Copia del estado (la actualiza la cola de VLC; ver refreshState). Leerla nunca bloquea.
static void refreshState(void) {
    if (!player) { atomic_store(&cachedPlaying, 0); return; }
    atomic_store(&cachedPlaying, libvlc_media_player_is_playing(player) ? 1 : 0);
    double length = libvlc_media_player_get_length(player) / 1000.0; if (length > 0) atomic_store(&cachedLength, length);
    if (CACurrentMediaTime() > seekHoldUntil) atomic_store(&cachedTime, libvlc_media_player_get_time(player) / 1000.0);
}
static void startStateTimer(void) {
    static dispatch_source_t timer; static dispatch_once_t once;
    dispatch_once(&once, ^{
        timer = dispatch_source_create(DISPATCH_SOURCE_TYPE_TIMER, 0, 0, vlcQueue());
        dispatch_source_set_timer(timer, dispatch_time(DISPATCH_TIME_NOW, 0), NSEC_PER_SEC / 5, NSEC_PER_SEC / 50);
        dispatch_source_set_event_handler(timer, ^{ refreshState(); });
        dispatch_resume(timer);
    });
}
int TraiggerMovieIsPlaying(void) { startStateTimer(); return atomic_load(&cachedPlaying); }
double TraiggerMovieTime(void) { startStateTimer(); return atomic_load(&cachedTime); }
double TraiggerMovieLength(void) { startStateTimer(); return atomic_load(&cachedLength); }
void TraiggerMovieSeek(double seconds) {
    atomic_store(&cachedTime, seconds); seekHoldUntil = CACurrentMediaTime() + 1.0;
    dispatch_async(vlcQueue(), ^{ if (player) libvlc_media_player_set_time(player, (int64_t)(seconds * 1000), false); });
}
void TraiggerMovieVolume(int percent) { wantedVolume = percent; audioGain = percent / 100.0f; }

// Último fotograma: devuelve su número (cambia con cada fotograma nuevo) y su tamaño.
// Visibles fuera de UnityFramework (el proyecto oculta los símbolos por defecto): las busca TraiggerMovieScreen con dlsym.
__attribute__((visibility("default"))) int64_t TraiggerMovieFrameInfo(int *width, int *height) {
    static int calls; if (++calls % 30 == 0) keepVolume();
    pthread_mutex_lock(&frameLock);
    *width = (int)frameWidth; *height = (int)frameHeight;
    int64_t value = latest >= 0 ? serial : -1;
    pthread_mutex_unlock(&frameLock);
    return value;
}
// Copia el último fotograma en el búfer de Unity (ancho × alto × 4 bytes). 1 si había fotograma.
__attribute__((visibility("default"))) int TraiggerMovieCopyFrame(uint8_t *destination, int capacity) {
    pthread_mutex_lock(&frameLock);
    if (latest < 0 || !buffers[latest] || capacity < (int)(frameWidth * frameHeight * 4)) { pthread_mutex_unlock(&frameLock); return 0; }
    reading = latest;
    uint8_t *source = buffers[reading];
    size_t size = (size_t)frameWidth * frameHeight * 4;
    pthread_mutex_unlock(&frameLock);
    memcpy(destination, source, size);
    pthread_mutex_lock(&frameLock); reading = -1; pthread_mutex_unlock(&frameLock);
    return 1;
}

// Explorar una carpeta (SMB u otra): empieza a leerla; el resultado se recoge con TraiggerMovieBrowseResult.
static libvlc_media_t *browsing;
int TraiggerMovieBrowse(const char *location) {
    ensureVLC();
    if (browsing) { libvlc_media_release(browsing); browsing = NULL; }
    browsing = libvlc_media_new_location(location);
    if (!browsing) return 0;
    return libvlc_media_parse_request(vlc, browsing, kParseLocal | kParseNetwork, 20000) == 0;
}
static void appendEscaped(char **json, size_t *length, size_t *capacity, const char *text) {
    for (const char *c = text ? text : ""; *c; c++) {
        char piece[8] = {0};
        if (*c == '"' || *c == '\\') snprintf(piece, sizeof piece, "\\%c", *c);
        else if ((unsigned char)*c < 0x20) snprintf(piece, sizeof piece, "\\u%04x", *c);
        else piece[0] = *c;
        size_t n = strlen(piece);
        if (*length + n + 64 > *capacity) { *capacity = (*capacity + n + 64) * 2; *json = realloc(*json, *capacity); }
        memcpy(*json + *length, piece, n); *length += n;
    }
}
static void appendRaw(char **json, size_t *length, size_t *capacity, const char *text) {
    size_t n = strlen(text);
    if (*length + n + 1 > *capacity) { *capacity = (*capacity + n + 64) * 2; *json = realloc(*json, *capacity); }
    memcpy(*json + *length, text, n); *length += n;
}
void TraiggerMovieFree(void *pointer) { free(pointer); }

// NULL mientras lee; después un JSON {"status":"ok|error","items":[{"name","url","folder"}]} (liberarlo con TraiggerMovieFree).
char *TraiggerMovieBrowseResult(void) {
    if (!browsing) return NULL;
    int status = libvlc_media_get_parsed_status(browsing);
    if (status != kParseDone && status != kParseFailed && status != kParseTimeout && status != kParseCancelled) return NULL;
    size_t length = 0, capacity = 256;
    char *json = malloc(capacity);
    appendRaw(&json, &length, &capacity, status == kParseDone ? "{\"status\":\"ok\",\"items\":[" : status == kParseTimeout ? "{\"status\":\"timeout\",\"items\":[" : "{\"status\":\"error\",\"items\":[");
    libvlc_media_list_t *list = status == kParseDone ? libvlc_media_subitems(browsing) : NULL;
    if (list) {
        libvlc_media_list_lock(list);
        int count = libvlc_media_list_count(list);
        for (int i = 0; i < count; i++) {
            libvlc_media_t *item = libvlc_media_list_item_at_index(list, i);
            if (!item) continue;
            char *mrl = libvlc_media_get_mrl(item), *title = libvlc_media_get_meta(item, kMetaTitle);
            appendRaw(&json, &length, &capacity, i ? ",{\"name\":\"" : "{\"name\":\"");
            appendEscaped(&json, &length, &capacity, title ? title : mrl);
            appendRaw(&json, &length, &capacity, "\",\"url\":\"");
            appendEscaped(&json, &length, &capacity, mrl);
            appendRaw(&json, &length, &capacity, libvlc_media_get_type(item) == kTypeDirectory ? "\",\"folder\":true}" : "\",\"folder\":false}");
            if (mrl) libvlc_free(mrl);
            if (title) libvlc_free(title);
            libvlc_media_release(item);
        }
        libvlc_media_list_unlock(list);
        libvlc_media_list_release(list);
    }
    appendRaw(&json, &length, &capacity, "]}");
    json[length] = 0;
    libvlc_media_release(browsing); browsing = NULL;
    return json;
}

// Pistas de audio (0) o de subtítulos (2): JSON [{"id","name","language","selected"}] (liberar con TraiggerMovieFree).
static char *tracksNow(int type) {
    size_t length = 0, capacity = 256;
    char *json = malloc(capacity);
    appendRaw(&json, &length, &capacity, "[");
    libvlc_media_tracklist_t *list = player ? libvlc_media_player_get_tracklist(player, (libvlc_track_type_t)type, false) : NULL;
    if (list) {
        size_t count = libvlc_media_tracklist_count(list);
        for (size_t i = 0; i < count; i++) {
            libvlc_media_track_t *track = libvlc_media_tracklist_at(list, i);
            appendRaw(&json, &length, &capacity, i ? ",{\"id\":\"" : "{\"id\":\"");
            appendEscaped(&json, &length, &capacity, track->psz_id);
            appendRaw(&json, &length, &capacity, "\",\"name\":\"");
            appendEscaped(&json, &length, &capacity, track->psz_name ? track->psz_name : track->psz_description);
            appendRaw(&json, &length, &capacity, "\",\"language\":\"");
            appendEscaped(&json, &length, &capacity, track->psz_language);
            appendRaw(&json, &length, &capacity, track->selected ? "\",\"selected\":true}" : "\",\"selected\":false}");
        }
        libvlc_media_tracklist_delete(list);
    }
    appendRaw(&json, &length, &capacity, "]");
    json[length] = 0;
    return json;
}
// Lista de pistas: se devuelve la última conocida al momento y se pide otra a la cola de VLC (sin esperar).
static char *cachedTracks[4];
static pthread_mutex_t tracksLock = PTHREAD_MUTEX_INITIALIZER;
char *TraiggerMovieTracks(int type) {
    if (type < 0 || type > 3) return strdup("[]");
    dispatch_async(vlcQueue(), ^{ char *json = tracksNow(type); pthread_mutex_lock(&tracksLock); free(cachedTracks[type]); cachedTracks[type] = json; pthread_mutex_unlock(&tracksLock); });
    pthread_mutex_lock(&tracksLock); char *copy = strdup(cachedTracks[type] ? cachedTracks[type] : "[]"); pthread_mutex_unlock(&tracksLock);
    return copy;
}
// Elige una pista por su id; id vacío o NULL desactiva ese tipo (por ejemplo, quitar los subtítulos).
static void selectTrackNow(int type, const char *identifier);
void TraiggerMovieSelectTrack(int type, const char *identifier) {
    char *copy = identifier ? strdup(identifier) : NULL;
    dispatch_async(vlcQueue(), ^{ selectTrackNow(type, copy); free(copy); });
}
static void selectTrackNow(int type, const char *identifier) {
    if (!player) return;
    if (!identifier || !*identifier) { libvlc_media_player_unselect_track_type(player, (libvlc_track_type_t)type); return; }
    libvlc_media_tracklist_t *list = libvlc_media_player_get_tracklist(player, (libvlc_track_type_t)type, false);
    if (!list) return;
    for (size_t i = 0; i < libvlc_media_tracklist_count(list); i++) {
        libvlc_media_track_t *track = libvlc_media_tracklist_at(list, i);
        if (track->psz_id && strcmp(track->psz_id, identifier) == 0) { libvlc_media_player_select_track(player, track); break; }
    }
    libvlc_media_tracklist_delete(list);
}
// Subtítulos de un archivo aparte (.srt, .ass…; URL smb:// o file://), seleccionados al cargarlos.
int TraiggerMovieAddSubtitle(const char *location) {
    if (!location) return 0;
    char *copy = strdup(location);
    dispatch_async(vlcQueue(), ^{ if (player) libvlc_media_player_add_slave(player, kSlaveSubtitle, copy, true); free(copy); });
    return 1;
}

// MARK: Escucha adelantada para los subtítulos generados (TraiggerSubtitles.swift)
// Un segundo reproductor, sin imagen ni sonido, decodifica el audio de la misma película unos segundos por delante de
// lo que se ve y lo entrega en 16 kHz mono con su posición en la película, para transcribirlo antes de que se diga.
extern void TraiggerSubtitleFeed(const int16_t *samples, int32_t count, double mediaTime);
extern void TraiggerSubtitleReset(double mediaTime);
extern void TraiggerSubtitleFinish(void);
static libvlc_media_player_t *listener;
static double listenerStart;
static int64_t listenerSamples;
static int listenerPaused, listenerActive;
static pthread_mutex_t listenerLock = PTHREAD_MUTEX_INITIALIZER;

static void listenPlay(void *data, const void *samples, unsigned count, int64_t pts) {
    pthread_mutex_lock(&listenerLock);
    double time = listenerStart + listenerSamples / 16000.0;
    listenerSamples += count;
    pthread_mutex_unlock(&listenerLock);
    TraiggerSubtitleFeed((const int16_t *)samples, (int32_t)count, time);
}
static void listenNothing(void *data, int64_t pts) {}
static void listenDrain(void *data) { TraiggerSubtitleFinish(); } // fin de la película: cerrar la última frase

static void listenStopNow(void) {
    if (!listener) return;
    libvlc_media_player_stop_async(listener);
    libvlc_media_player_release(listener);
    listener = NULL; listenerPaused = 0;
}
void TraiggerListenStop(void) { pthread_mutex_lock(&listenerLock); listenerActive = 0; pthread_mutex_unlock(&listenerLock); dispatch_async(vlcQueue(), ^{ listenStopNow(); }); }
// Empieza a escuchar la película actual desde «from» segundos.
static int listenStartNow(double from);
int TraiggerListenStart(double from) {
    pthread_mutex_lock(&listenerLock); listenerActive = 1; listenerStart = from < 0 ? 0 : from; listenerSamples = 0; pthread_mutex_unlock(&listenerLock);
    dispatch_async(vlcQueue(), ^{ listenStartNow(from); });
    return 1;
}
static int listenStartNow(double from) {
    ensureVLC();
    listenStopNow();
    if (!currentLocation) return 0;
    libvlc_media_t *media = strstr(currentLocation, "://") ? libvlc_media_new_location(currentLocation) : libvlc_media_new_path(currentLocation);
    if (!media) return 0;
    char option[64];
    snprintf(option, sizeof option, ":start-time=%.3f", from < 0 ? 0 : from);
    libvlc_media_add_option(media, ":no-video");
    libvlc_media_add_option(media, ":no-sub-autodetect-file");
    libvlc_media_add_option(media, option);
    listener = libvlc_media_player_new(vlc);
    libvlc_media_player_set_media(listener, media);
    libvlc_media_release(media);
    libvlc_audio_set_callbacks(listener, listenPlay, listenNothing, listenNothing, listenNothing, listenDrain, NULL);
    libvlc_audio_set_format(listener, "S16N", 16000, 1);
    pthread_mutex_lock(&listenerLock);
    listenerStart = from < 0 ? 0 : from; listenerSamples = 0;
    pthread_mutex_unlock(&listenerLock);
    TraiggerSubtitleReset(listenerStart);
    return libvlc_media_player_play(listener) == 0;
}
// Posición (en la película) del audio que ya se ha escuchado.
double TraiggerListenTime(void) {
    pthread_mutex_lock(&listenerLock);
    double time = listenerActive ? listenerStart + listenerSamples / 16000.0 : -1;
    pthread_mutex_unlock(&listenerLock);
    return time;
}
void TraiggerListenPause(int paused) {
    dispatch_async(vlcQueue(), ^{
        if (!listener || paused == listenerPaused) return;
        listenerPaused = paused;
        libvlc_media_player_set_pause(listener, paused);
    });
}
