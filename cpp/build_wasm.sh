#!/bin/bash

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

setup()
{
    if [ ! -d "$HOME/emsdk" ]; then
        echo "emsdk not found in $HOME/emsdk, cloning and installing..."
        git clone https://github.com/emscripten-core/emsdk.git "$HOME/emsdk"
        cd "$HOME/emsdk" || exit 1
        ./emsdk install latest
        ./emsdk activate latest
    fi
}

ensure_emsdk_env()
{
    if [ -f "$HOME/emsdk/emsdk_env.sh" ]; then
        # shellcheck source=/dev/null
        source "$HOME/emsdk/emsdk_env.sh"
    else
        echo "ERROR: $HOME/emsdk/emsdk_env.sh not found. Run the script again or run 'cd $HOME/emsdk && ./emsdk install latest && ./emsdk activate latest' manually."
        exit 1
    fi
}

ensure_bazel()
{
    if ! command -v bazel >/dev/null 2>&1; then
        echo "bazel not found in PATH. Attempting to provision bazelisk (platform-agnostic)."
        if command -v brew >/dev/null 2>&1; then
            echo "Homebrew detected, installing bazelisk via Homebrew..."
            brew install bazelisk || {
                echo "Homebrew install failed; will try to download bazelisk binary instead.";
            }
        fi

        if command -v bazel >/dev/null 2>&1; then
            echo "bazel now available."
            return 0
        fi

        # Determine OS/arch for download fallback
        UNAME_S=$(uname -s)
        UNAME_M=$(uname -m)
        ARCH=""
        OSNAME=""
        case "$UNAME_S" in
            Darwin) OSNAME="darwin" ;;
            Linux) OSNAME="linux" ;;
            MINGW*|MSYS*|CYGWIN*) OSNAME="windows" ;;
            *) OSNAME="linux" ;;
        esac
        case "$UNAME_M" in
            x86_64|amd64) ARCH="amd64" ;;
            aarch64|arm64) ARCH="arm64" ;;
            *) ARCH="amd64" ;;
        esac

        ASSET_NAME="bazelisk-${OSNAME}-${ARCH}"
        if [ "$OSNAME" = "windows" ]; then
            ASSET_NAME="${ASSET_NAME}.exe"
            DEST_BIN="$HOME/.local/bin/bazel.exe"
        else
            DEST_BIN="$HOME/.local/bin/bazel"
        fi

        DOWNLOAD_URL="https://github.com/bazelbuild/bazelisk/releases/latest/download/${ASSET_NAME}"

        mkdir -p "$HOME/.local/bin"
        if command -v curl >/dev/null 2>&1; then
            echo "Downloading bazelisk from $DOWNLOAD_URL to $DEST_BIN"
            curl -L -f -o "$DEST_BIN" "$DOWNLOAD_URL" || {
                echo "Download failed. Please install Bazel or Bazelisk manually and re-run the script.";
                exit 1
            }
        elif command -v wget >/dev/null 2>&1; then
            echo "Downloading bazelisk from $DOWNLOAD_URL to $DEST_BIN"
            wget -O "$DEST_BIN" "$DOWNLOAD_URL" || {
                echo "Download failed. Please install Bazel or Bazelisk manually and re-run the script.";
                exit 1
            }
        else
            echo "Neither curl nor wget available to download bazelisk. Please install one and re-run the script.";
            exit 1
        fi

        chmod +x "$DEST_BIN"
        export PATH="$HOME/.local/bin:$PATH"
        if command -v bazel >/dev/null 2>&1; then
            echo "bazelisk installed to $DEST_BIN and available on PATH."
        else
            echo "Installed bazelisk to $DEST_BIN but it's not available as 'bazel' on PATH. Ensure $HOME/.local/bin is in your PATH and re-run the script.";
            exit 1
        fi
    fi
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

setup
ensure_emsdk_env
ensure_bazel

build
