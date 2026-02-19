#include <emscripten/bind.h>
#include <memory>
#include <string>
#include <vector>

#include "worldline/classic/resampler.h"
#include "worldline/f0/dio_estimator.h"
#include "worldline/f0/dio_ss_estimator.h"
#include "worldline/f0/harvest_estimator.h"
#include "worldline/f0/pyin_estimator.h"
#include "worldline/model/model.h"
#include "worldline/synth_request.h"

using namespace worldline;
using namespace emscripten;

int get_version() {
    return 1;
}

std::unique_ptr<Resampler> create_resampler(double pitch_shift, double velocity) {
    std::vector<std::string> args = {
        "worldline", "", "", 
        std::to_string(pitch_shift),
        std::to_string(velocity)
    };
    return std::make_unique<Resampler>(args);
}

EMSCRIPTEN_BINDINGS(worldline) {
    function("getVersion", &get_version);
    function("createResampler", &create_resampler);

    class_<HarvestEstimator>("HarvestEstimator")
        .constructor<>();

    class_<DioEstimator>("DioEstimator")
        .constructor<>();

    class_<DioSsEstimator>("DioSsEstimator")
        .constructor<>();

    class_<PyinEstimator>("PyinEstimator")
        .constructor<>();

    class_<Model>("Model")
        .constructor<int, double, int>()
        .function("buildF0", &Model::BuildF0)
        .function("buildSp", &Model::BuildSp)
        .function("buildAp", &Model::BuildAp)
        .function("buildResidual", &Model::BuildResidual)
        .function("synthPlatinum", &Model::SynthPlatinum)
        .function("trim", &Model::Trim)
        .function("remap", &Model::Remap)
        .function("getVoicedRatio", &Model::GetVoicedRatio)
        .function("msToSamples", &Model::MsToSamples)
        .function("fs", &Model::fs)
        .function("totalMs", &Model::total_ms)
        .function("frameMs", &Model::frame_ms)
        .function("fftSize", &Model::fft_size);

    class_<Resampler>("Resampler")
        .function("resample", &Resampler::Resample);

    class_<SynthRequest>("SynthRequest")
        .constructor<>()
        .property("sample_fs", &SynthRequest::sample_fs)
        .property("sample_length", &SynthRequest::sample_length)
        .property("tone", &SynthRequest::tone)
        .property("con_vel", &SynthRequest::con_vel)
        .property("offset", &SynthRequest::offset)
        .property("required_length", &SynthRequest::required_length)
        .property("consonant", &SynthRequest::consonant)
        .property("cut_off", &SynthRequest::cut_off)
        .property("volume", &SynthRequest::volume)
        .property("modulation", &SynthRequest::modulation)
        .property("tempo", &SynthRequest::tempo)
        .property("flag_g", &SynthRequest::flag_g)
        .property("flag_O", &SynthRequest::flag_O)
        .property("flag_P", &SynthRequest::flag_P)
        .property("flag_Mt", &SynthRequest::flag_Mt)
        .property("flag_Mb", &SynthRequest::flag_Mb)
        .property("flag_Mv", &SynthRequest::flag_Mv);
}
