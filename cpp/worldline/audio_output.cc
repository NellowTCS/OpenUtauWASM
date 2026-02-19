#include "worldline/audio_output.h"

#include <stdint.h>

#define MINIAUDIO_IMPLEMENTATION
#include "miniaudio.h"
#include "xxhash.h"

DLL_API int32_t ou_get_audio_device_infos(ou_audio_device_info_t* device_infos,
                                          int32_t max_count) {
  ma_context context;
  ma_backend backends[1];

  int32_t device_count = 0;

  for (int i = 0; i < ma_backend_null; i++) {
    backends[0] = (ma_backend)i;
    ma_result result = ma_context_init(backends, 1, NULL, &context);
    if (result != MA_SUCCESS) {
      continue;
    }

    ma_device_info* playback_device_infos;
    ma_uint32 playback_device_count;
    ma_device_info* capture_device_infos;
    ma_uint32 capture_device_count;
    result = ma_context_get_devices(
        &context, &playback_device_infos, &playback_device_count,
        &capture_device_infos, &capture_device_count);
    if (result != MA_SUCCESS) {
      ma_context_uninit(&context);
      continue;
    }

    for (int j = 0; j < playback_device_count; j++) {
      if (device_count >= max_count) {
        device_count++;
        break;
      }

      ma_device_info* info = &playback_device_infos[j];
      device_infos[device_count].name = strdup(info->name);
      device_infos[device_count].id =
          XXH64(&(info->id), sizeof(ma_device_id), 0);
      device_infos[device_count].api =
          strdup(ma_get_backend_name(context.backend));
      device_infos[device_count].api_id = context.backend;
      device_count++;
    }

    ma_context_uninit(&context);
  }
  return device_count;
}

DLL_API void ou_free_audio_device_infos(ou_audio_device_info_t* device_infos,
                                        int32_t count) {
  for (int32_t i = 0; i < count; i++) {
    free(device_infos[i].name);
    free(device_infos[i].api);
  }
}

static ou_audio_data_callback_t g_data_callback = NULL;

static void silence(float* buffer, uint32_t channels, uint32_t frame_count) {
  memset(buffer, 0, channels * frame_count * sizeof(float));
}

static void data_callback(ma_device* pDevice, void* pOutput, const void* pInput,
                          ma_uint32 frameCount) {
  if (g_data_callback == NULL) {
    silence((float*)pOutput, pDevice->playback.channels, frameCount);
  } else {
    g_data_callback((float*)pOutput, pDevice->playback.channels, frameCount);
  }
}

DLL_API ou_audio_context_t* ou_init_audio_device(
    uint32_t api_id, uint64_t id, ou_audio_data_callback_t callback) {
  ou_audio_context_t* result = new ou_audio_context_t();
  if (result == NULL) {
    return NULL;
  }

  ma_backend backends[1] = {(ma_backend)api_id};
  if (ma_context_init(backends, 1, NULL, &result->context) != MA_SUCCESS) {
    delete result;
    return NULL;
  }

  ma_device_info* playback_device_infos;
  ma_uint32 playback_device_count;
  ma_device_info* capture_device_infos;
  ma_uint32 capture_device_count;
  if (ma_context_get_devices(&result->context, &playback_device_infos,
                             &playback_device_count, &capture_device_infos,
                             &capture_device_count) != MA_SUCCESS) {
    ma_context_uninit(&result->context);
    delete result;
    return NULL;
  }

  ma_device_config config = ma_device_config_init(ma_device_type_playback);
  config.playback.format = ma_format_f32;
  config.playback.channels = 2;
  config.sampleRate = 44100;
  g_data_callback = callback;
  config.dataCallback = data_callback;
  config.pUserData = result;

  for (ma_uint32 i = 0; i < playback_device_count; i++) {
    ma_device_info* info = &playback_device_infos[i];
    if (XXH64(&(info->id), sizeof(ma_device_id), 0) == id) {
      config.playback.pDeviceID = &info->id;
      break;
    }
  }

  if (config.playback.pDeviceID == NULL) {
    ma_context_uninit(&result->context);
    delete result;
    return NULL;
  }

  if (ma_device_init(&result->context, &config, &result->device) !=
      MA_SUCCESS) {
    ma_context_uninit(&result->context);
    delete result;
    return NULL;
  }

  return result;
}

DLL_API ou_audio_context_t* ou_init_audio_device_auto(
    ou_audio_data_callback_t callback) {
  ou_audio_context_t* result = new ou_audio_context_t();
  if (result == NULL) {
    return NULL;
  }

  ma_device_config config = ma_device_config_init(ma_device_type_playback);
  config.playback.format = ma_format_f32;
  config.playback.channels = 2;
  config.sampleRate = 44100;
  g_data_callback = callback;
  config.dataCallback = data_callback;
  config.pUserData = result;

  if (ma_device_init(NULL, &config, &result->device) != MA_SUCCESS) {
    delete result;
    return NULL;
  }

  return result;
}

DLL_API const char* ou_get_audio_device_api(ou_audio_context_t* context) {
  ma_backend backend = context->device.pContext->backend;
  return ma_get_backend_name(backend);
}

DLL_API const char* ou_get_audio_device_name(ou_audio_context_t* context) {
  return context->device.playback.name;
}

DLL_API int ou_free_audio_device(ou_audio_context_t* context) {
  bool release_context = !context->device.isOwnerOfContext;
  ma_device_uninit(&context->device);
  if (release_context) {
    ma_result result = ma_context_uninit(&context->context);
    if (result != MA_SUCCESS) {
      return result;
    }
  }
  delete context;
  return 0;
}

DLL_API int ou_audio_device_start(ou_audio_context_t* context) {
  return ma_device_start(&context->device);
}

DLL_API int ou_audio_device_stop(ou_audio_context_t* context) {
  return ma_device_stop(&context->device);
}

DLL_API const char* ou_audio_get_error_message(int error_code) {
  return ma_result_description((ma_result)error_code);
}

DLL_API ou_audio_decoder_t* ou_audio_decoder_open(const char* filename) {
  ou_audio_decoder_t* decoder = new ou_audio_decoder_t();
  if (decoder == NULL) {
    return NULL;
  }
  
  ma_result result = ma_decoder_init_file(filename, NULL, &decoder->decoder);
  if (result != MA_SUCCESS) {
    delete decoder;
    return NULL;
  }
  decoder->is_open = true;
  return decoder;
}

DLL_API void ou_audio_decoder_close(ou_audio_decoder_t* decoder) {
  if (decoder) {
    ma_decoder_uninit(&decoder->decoder);
    delete decoder;
  }
}

DLL_API int ou_audio_decoder_get_sample_rate(ou_audio_decoder_t* decoder) {
  if (!decoder) return 0;
  return decoder->decoder.outputSampleRate;
}

DLL_API int ou_audio_decoder_get_channels(ou_audio_decoder_t* decoder) {
  if (!decoder) return 0;
  return decoder->decoder.outputChannels;
}

DLL_API int ou_audio_decoder_get_length(ou_audio_decoder_t* decoder) {
  if (!decoder) return 0;
  ma_uint64 length = 0;
  ma_decoder_get_length_in_pcm_frames(&decoder->decoder, &length);
  return (int)length;
}

DLL_API int ou_audio_decoder_read(ou_audio_decoder_t* decoder, float* buffer, int frame_count) {
  if (!decoder || !buffer) return 0;
  
  ma_uint64 framesRead = 0;
  ma_result result = ma_decoder_read_pcm_frames(&decoder->decoder, buffer, frame_count, &framesRead);
  if (result != MA_SUCCESS) {
    return -1;
  }
  
  return (int)framesRead;
}

DLL_API int ou_audio_decoder_seek(ou_audio_decoder_t* decoder, int frame_position) {
  if (!decoder) return -1;
  
  ma_result result = ma_decoder_seek_to_pcm_frame(&decoder->decoder, frame_position);
  if (result != MA_SUCCESS) {
    return -1;
  }
  
  return 0;
}
