// La capturadora y la emisión (Plugins/Apple) esperan al reproductor de películas de Unity, que en esta app no existe.
#include <stdint.h>
typedef void (*TraiggerAudioSink)(const int16_t *, int32_t);
void TraiggerMovieSetStreamSink(TraiggerAudioSink sink) { (void)sink; }
