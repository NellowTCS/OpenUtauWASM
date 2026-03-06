#ifndef WORLDLINE_AUDIO_OUTPUT_H
#define WORLDLINE_AUDIO_OUTPUT_H

#include <cstdint>

#include "miniaudio.h"

#if defined(_MSC_VER)
#define DLL_API __declspec(dllexport)
#elif defined(__GNUC__)
#define DLL_API __attribute__((visibility("default")))
#endif

#if defined(__cplusplus)
extern "C" {
#endif

struct ou_audio_device_info_t {
  char* name;
  uint64_t id;
  char* api;
  uint32_t api_id;
};

struct ou_audio_context_t {
  ma_context context;
  ma_device device;
};

struct ou_audio_decoder_t {
  ma_decoder decoder;
  bool is_open;
};

typedef void (*ou_audio_data_callback_t)(float* buffer, uint32_t channels,
                                         uint32_t frame_count);

DLL_API int32_t ou_get_audio_device_infos(ou_audio_device_info_t* device_infos,
                                          int32_t max_count);

DLL_API void ou_free_audio_device_infos(ou_audio_device_info_t* device_infos,
                                        int32_t count);

DLL_API ou_audio_context_t* ou_init_audio_device(
    uint32_t api_id, uint64_t id, ou_audio_data_callback_t callback);

DLL_API ou_audio_context_t* ou_init_audio_device_auto(
    ou_audio_data_callback_t callback);

DLL_API const char* ou_get_audio_device_api(ou_audio_context_t* context);

// On windows returns string of local code page, except for WASAPI which returns UTF-8.
// On other platforms returns UTF-8.
DLL_API const char* ou_get_audio_device_name(ou_audio_context_t* context);

DLL_API int ou_free_audio_device(ou_audio_context_t* context);

DLL_API int ou_audio_device_start(ou_audio_context_t* context);

DLL_API int ou_audio_device_stop(ou_audio_context_t* context);

DLL_API void ou_audio_device_set_callback(ou_audio_context_t* context, ou_audio_data_callback_t callback);

DLL_API const char* ou_audio_get_error_message(int error_code);

DLL_API ou_audio_decoder_t* ou_audio_decoder_open(const char* filename);

DLL_API ou_audio_decoder_t* ou_audio_decoder_open_memory(const void* data, size_t data_size);

DLL_API void ou_audio_decoder_close(ou_audio_decoder_t* decoder);

DLL_API int ou_audio_decoder_get_sample_rate(ou_audio_decoder_t* decoder);

DLL_API int ou_audio_decoder_get_channels(ou_audio_decoder_t* decoder);

DLL_API int ou_audio_decoder_get_length(ou_audio_decoder_t* decoder);

DLL_API int ou_audio_decoder_read(ou_audio_decoder_t* decoder, float* buffer, int frames);

DLL_API int ou_audio_decoder_seek(ou_audio_decoder_t* decoder, int frame);

DLL_API int ou_audio_decoder_is_at_end(ou_audio_decoder_t* decoder);

#if defined(__cplusplus)
}
#endif

#endif  // WORLDLINE_AUDIO_OUTPUT_H
