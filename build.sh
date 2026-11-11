#!/bin/bash
# 编译 QuotaBar 并安装到 /Applications。
#
# 用法：
#   ./build.sh            # Release
#   ./build.sh debug      # Debug

set -euo pipefail

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CONFIG="Release"
if [[ "${1:-}" == "debug" ]]; then
    CONFIG="Debug"
fi

BUILD_DIR="${PROJECT_DIR}/.build"
DERIVED="${BUILD_DIR}/DerivedData"
APP_SOURCE="${DERIVED}/Build/Products/${CONFIG}/QuotaBar.app"
INSTALL_PATH="/Applications/QuotaBar.app"

echo "==> 编译 ${CONFIG}"
xcodebuild \
    -project "${PROJECT_DIR}/QuotaBar.xcodeproj" \
    -scheme QuotaBar \
    -configuration "${CONFIG}" \
    -derivedDataPath "${DERIVED}" \
    build \
    | grep -E "error:|warning: .*\.swift|BUILD " || true

if [[ ! -d "${APP_SOURCE}" ]]; then
    echo "构建失败：找不到 ${APP_SOURCE}" >&2
    exit 1
fi

echo "==> 安装到 ${INSTALL_PATH}"
if [[ -d "${INSTALL_PATH}" ]]; then
    echo "    已存在旧版本，先退出它"
    osascript -e 'quit app "QuotaBar"' 2>/dev/null || true
    sleep 1
    rm -rf "${INSTALL_PATH}"
fi
ditto "${APP_SOURCE}" "${INSTALL_PATH}"

# 重新注册小组件扩展，让系统立刻看到新版本
pluginkit -a "${INSTALL_PATH}/Contents/PlugIns/QuotaBarWidget.appex" 2>/dev/null || true

echo "==> 完成"
echo "    打开：open \"${INSTALL_PATH}\""
echo "    加小组件：右键桌面 → 编辑小组件 → 搜索 QuotaBar"
