#!/bin/bash

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

setup()
{
    if [ ! -d "$HOME/emsdk" ]; then
        git clone https://github.com/emscripten-core/emsdk.git "$HOME/emsdk"
        cd "$HOME/emsdk"
        ./emsdk install latest
        ./emsdk activate latest
    fi
    
    source "$HOME/emsdk/emsdk_env.sh"
}

build()
{
    cd "$SCRIPT_DIR"
    
    mkdir -p ../runtimes/wasm
    
    bazel build //worldline:worldline_wasm --config=wasm -c opt
    
    mkdir -p ../runtimes/wasm/native
    tar -xf bazel-bin/worldline/worldline_wasm -C ../runtimes/wasm/native
    
    cp ../runtimes/wasm/native/worldline_wasm.js ../runtimes/wasm/
    cp ../runtimes/wasm/native/worldline_wasm.wasm ../runtimes/wasm/
    
    echo "Built files:"
    ls -la ../runtimes/wasm/
}

source "$HOME/emsdk/emsdk_env.sh"

build
