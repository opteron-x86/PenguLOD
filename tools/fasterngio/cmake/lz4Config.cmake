# Distributions that ship lz4 without its CMake package.
find_path(LZ4_INCLUDE_DIR lz4.h REQUIRED)
find_library(LZ4_LIBRARY NAMES lz4 REQUIRED)
if(NOT TARGET lz4::lz4)
  add_library(lz4::lz4 UNKNOWN IMPORTED)
  set_target_properties(lz4::lz4 PROPERTIES IMPORTED_LOCATION "${LZ4_LIBRARY}"
    INTERFACE_INCLUDE_DIRECTORIES "${LZ4_INCLUDE_DIR}")
endif()
