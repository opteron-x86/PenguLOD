#pragma once
#include <algorithm>
#include <array>
#include <atomic>
#include <chrono>
#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <filesystem>
#include <format>
#include <fstream>
#include <iterator>
#include <map>
#include <optional>
#include <string>
#include <vector>
// The file readers only need logging from SKSE; tests exercise their actual parsers.
namespace logger {
    template<class... T> void error(T&&...) {}
    template<class... T> void warn(T&&...) {}
    template<class... T> void info(T&&...) {}
}
#include "RuntimePaths.h"
#include "DataFile.h"
#include "IniFile.h"
