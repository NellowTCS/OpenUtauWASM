#include <emscripten.h>
#include <cstring>
#include <cstdlib>

#include "worldline/worldline.h"
#include "worldline/synth_request.h"
#include "worldline/audio_output.h"

extern "C" {

struct PhraseSynthWrapper {
    PhraseSynth* ptr;
};

struct AudioDecoderWrapper {
    ou_audio_decoder_t* decoder;
};

// Version
EMSCRIPTEN_KEEPALIVE
int worldline_version() {
    return 1;
}

// main resample fn
// Returns allocated float array (caller must free with Module._free)
EMSCRIPTEN_KEEPALIVE
float* worldline_resample(
    float* samples, int sample_len,
    int sample_rate,
    double pitch_shift,
    double velocity,
    double volume,
    double offset_ms,
    double length_ms,
    double consonant_ms,
    double cutoff_ms,
    double modulation,
    int tone,
    int flag_g,
    int flag_P,
    int flag_Mt,
    int flag_Mb,
    int flag_Mv,
    int* out_length
) {
    SynthRequest request = {};
    request.sample_fs = sample_rate;
    request.sample_length = sample_len;
    request.sample = (double*)samples;
    request.tone = tone;
    request.con_vel = velocity;
    request.offset = offset_ms;
    request.required_length = length_ms;
    request.consonant = consonant_ms;
    request.cut_off = cutoff_ms;
    request.volume = volume;
    request.modulation = modulation;
    request.tempo = 120.0;
    
    request.flag_g = flag_g;
    request.flag_O = 0;
    request.flag_P = flag_P;
    request.flag_Mt = flag_Mt;
    request.flag_Mb = flag_Mb;
    request.flag_Mv = flag_Mv;
    
    float* output = nullptr;
    *out_length = Resample(&request, &output);
    
    return output;
}

// F0 estimation, returns f0 array (caller must free)
// method: 0=DIO, 1=Harvest, 2=Pyin
EMSCRIPTEN_KEEPALIVE
float* worldline_f0(
    float* samples, int sample_len,
    int sample_rate,
    double frame_ms,
    int method,
    int* out_length
) {
    double* f0 = nullptr;
    *out_length = F0(samples, sample_len, sample_rate, frame_ms, method, &f0);
    
    float* result = (float*)malloc(*out_length * sizeof(float));
    for (int i = 0; i < *out_length; i++) {
        result[i] = (float)f0[i];
    }
    free(f0);
    
    return result;
}

// DecodeMgc, returns spectrogram as flat double array
// Caller must free result
EMSCRIPTEN_KEEPALIVE
double* worldline_decode_mgc(
    int f0_length,
    double* mgc, int mgc_size,
    int fft_size, int fs,
    int* out_size
) {
    double* spectrogram = nullptr;
    int size = DecodeMgc(f0_length, mgc, mgc_size, fft_size, fs, &spectrogram);
    
    *out_size = size;
    return spectrogram;
}

// DecodeBap, returns aperiodicity as flat double array
// Caller must free result
EMSCRIPTEN_KEEPALIVE
double* worldline_decode_bap(
    int f0_length,
    double* bap, int fft_size, int fs,
    int* out_size
) {
    double* aperiodicity = nullptr;
    int size = DecodeBap(f0_length, bap, fft_size, fs, &aperiodicity);
    
    *out_size = size;
    return aperiodicity;
}

// Initialize analysis config
EMSCRIPTEN_KEEPALIVE
void worldline_init_config(
    int fs, int hop_size, int fft_size,
    int* out_fs, int* out_hop_size, int* out_fft_size,
    float* out_f0_floor, double* out_frame_ms
) {
    AnalysisConfig config;
    InitAnalysisConfig(&config, fs, hop_size, fft_size);
    
    *out_fs = config.fs;
    *out_hop_size = config.hop_size;
    *out_fft_size = config.fft_size;
    *out_f0_floor = config.f0_floor;
    *out_frame_ms = config.frame_ms;
}

// Full WORLD analysis, returns flat arrays
// Caller must free all outputs
EMSCRIPTEN_KEEPALIVE
int worldline_analyze(
    float* samples, int sample_len,
    int fs, int hop_size, int fft_size,
    double* f0_out,  // pre-allocated, size = num_frames
    double* sp_out,   // pre-allocated, size = num_frames * (fft_size/2+1)
    double* ap_out    // pre-allocated, size = num_frames * (fft_size/2+1)
) {
    AnalysisConfig config;
    InitAnalysisConfig(&config, fs, hop_size, fft_size);
    
    double* f0 = nullptr;
    double* sp = nullptr;
    double* ap = nullptr;
    int num_frames = 0;
    
    WorldAnalysis(&config, samples, sample_len, &f0, &sp, &ap, &num_frames);
    
    int sp_size = fft_size / 2 + 1;
    
    // Copy to output buffers
    memcpy(f0_out, f0, num_frames * sizeof(double));
    memcpy(sp_out, sp, num_frames * sp_size * sizeof(double));
    memcpy(ap_out, ap, num_frames * sp_size * sizeof(double));
    
    free(f0);
    free(sp);
    free(ap);
    
    return num_frames;
}

// WORLD analysis with F0 input
EMSCRIPTEN_KEEPALIVE
void worldline_analyze_f0_in(
    float* samples, int sample_len,
    int fs, int hop_size, int fft_size,
    double* f0_in, int f0_length,
    double* sp_out,   // pre-allocated, size = f0_length * (fft_size/2+1)
    double* ap_out    // pre-allocated, size = f0_length * (fft_size/2+1)
) {
    AnalysisConfig config;
    InitAnalysisConfig(&config, fs, hop_size, fft_size);
    
    WorldAnalysisF0In(&config, samples, sample_len, f0_in, f0_length, sp_out, ap_out);
}

// WORLD synthesis, returns float array
// Caller must free result
EMSCRIPTEN_KEEPALIVE
float* worldline_synthesize(
    double* f0, int f0_length,
    double* sp, int sp_size,  // fft_size/2+1, not array length
    double* ap, int fft_size,
    int fs, double frame_period,
    double* gender, double* tension, double* breathiness, double* voicing,
    int* out_length
) {
    double* output = nullptr;
    double** output_ptr = &output;
    
    int length = WorldSynthesis(
        f0, f0_length,
        sp, false, sp_size,
        ap, false, fft_size,
        frame_period, fs, output_ptr,
        gender, tension, breathiness, voicing
    );
    
    float* result = (float*)malloc(length * sizeof(float));
    for (int i = 0; i < length; i++) {
        result[i] = (float)output[i];
    }
    free(output);
    
    *out_length = length;
    return result;
}

EMSCRIPTEN_KEEPALIVE
PhraseSynthWrapper* worldline_phrase_synth_new() {
    PhraseSynthWrapper* wrapper = (PhraseSynthWrapper*)malloc(sizeof(PhraseSynthWrapper));
    wrapper->ptr = PhraseSynthNew();
    return wrapper;
}

EMSCRIPTEN_KEEPALIVE
void worldline_phrase_synth_delete(PhraseSynthWrapper* wrapper) {
    if (wrapper && wrapper->ptr) {
        PhraseSynthDelete(wrapper->ptr);
    }
    free(wrapper);
}

EMSCRIPTEN_KEEPALIVE
void worldline_phrase_synth_add_request(
    PhraseSynthWrapper* wrapper,
    float* samples, int sample_len,
    int sample_rate,
    int tone,
    double velocity, double offset, double required_length,
    double consonant, double cut_off, double volume, double modulation, double tempo,
    int flag_g, int flag_O, int flag_P, int flag_Mt, int flag_Mb, int flag_Mv,
    double pos_ms, double skip_ms, double length_ms,
    double fade_in_ms, double fade_out_ms
) {
    if (!wrapper || !wrapper->ptr) return;
    
    SynthRequest request = {};
    request.sample_fs = sample_rate;
    request.sample_length = sample_len;
    request.sample = (double*)samples;
    request.tone = tone;
    request.con_vel = velocity;
    request.offset = offset;
    request.required_length = required_length;
    request.consonant = consonant;
    request.cut_off = cut_off;
    request.volume = volume;
    request.modulation = modulation;
    request.tempo = tempo;
    request.flag_g = flag_g;
    request.flag_O = flag_O;
    request.flag_P = flag_P;
    request.flag_Mt = flag_Mt;
    request.flag_Mb = flag_Mb;
    request.flag_Mv = flag_Mv;
    
    PhraseSynthAddRequest(wrapper->ptr, &request, pos_ms, skip_ms, length_ms, fade_in_ms, fade_out_ms, nullptr);
}

EMSCRIPTEN_KEEPALIVE
void worldline_phrase_synth_set_curves(
    PhraseSynthWrapper* wrapper,
    double* f0, double* gender, double* tension, double* breathiness, double* voicing,
    int length
) {
    if (!wrapper || !wrapper->ptr) return;
    PhraseSynthSetCurves(wrapper->ptr, f0, gender, tension, breathiness, voicing, length, nullptr);
}

EMSCRIPTEN_KEEPALIVE
float* worldline_phrase_synth_synth(PhraseSynthWrapper* wrapper, int* out_length) {
    if (!wrapper || !wrapper->ptr) {
        *out_length = 0;
        return nullptr;
    }
    
    float* output = nullptr;
    int length = PhraseSynthSynth(wrapper->ptr, &output, nullptr);
    
    *out_length = length;
    return output;
}

EMSCRIPTEN_KEEPALIVE
AudioDecoderWrapper* worldline_audio_decoder_init_file(const char* filename) {
    AudioDecoderWrapper* wrapper = (AudioDecoderWrapper*)malloc(sizeof(AudioDecoderWrapper));
    wrapper->decoder = ou_audio_decoder_open(filename);
    if (!wrapper->decoder) {
        free(wrapper);
        return nullptr;
    }
    return wrapper;
}

EMSCRIPTEN_KEEPALIVE
void worldline_audio_decoder_free(AudioDecoderWrapper* wrapper) {
    if (wrapper) {
        if (wrapper->decoder) {
            ou_audio_decoder_close(wrapper->decoder);
        }
        free(wrapper);
    }
}

EMSCRIPTEN_KEEPALIVE
int worldline_audio_decoder_get_sample_rate(AudioDecoderWrapper* wrapper) {
    if (!wrapper || !wrapper->decoder) return 0;
    return ou_audio_decoder_get_sample_rate(wrapper->decoder);
}

EMSCRIPTEN_KEEPALIVE
int worldline_audio_decoder_get_channels(AudioDecoderWrapper* wrapper) {
    if (!wrapper || !wrapper->decoder) return 0;
    return ou_audio_decoder_get_channels(wrapper->decoder);
}

EMSCRIPTEN_KEEPALIVE
int worldline_audio_decoder_get_frame_count(AudioDecoderWrapper* wrapper) {
    if (!wrapper || !wrapper->decoder) return 0;
    return ou_audio_decoder_get_length(wrapper->decoder);
}

EMSCRIPTEN_KEEPALIVE
int worldline_audio_decoder_read(AudioDecoderWrapper* wrapper, float* buffer, int frame_count) {
    if (!wrapper || !wrapper->decoder || !buffer) return 0;
    return ou_audio_decoder_read(wrapper->decoder, buffer, frame_count);
}

EMSCRIPTEN_KEEPALIVE
int worldline_audio_decoder_seek(AudioDecoderWrapper* wrapper, int frame_position) {
    if (!wrapper || !wrapper->decoder) return -1;
    return ou_audio_decoder_seek(wrapper->decoder, frame_position);
}

} // extern "C"
