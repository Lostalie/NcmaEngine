#指定 CMake 的最低版本要求
cmake_minimum_required(VERSION 4.0)

#定义项目的名称
project(NcmaEngine VERSION 0.0.1)

#指定语言标准
set(CMAKE_CXX_STANDARD 17)
#指定编辑器必须支持指定的语言标准
set(CMAKE_CXX_STANDARD_REQUIRED ON)
#off默认生成静态库 on生成动态库
set(BUILD_SHARED_LIBS OFF)

#允许你定义一个选项，该选项的默认值和可用性取决于某些前置条件 应用场景：某个功能仅在特定平台或特定配置下可用 选项之间的依赖关系 自动禁用无效选项
include(CMakeDependentOption)

#PROJECT_SOURCE_DIR包含顶层CMakeLists文件的目录
#PROJECT_BINARY_DIR执行构建命令的目录
#判断两个目录是否相同，如果相同则弹出提示
if(PROJECT_SOURCE_DIR STREQUAL PROJECT_BINARY_DIR)
  message(
    FATAL_ERROR
      "In-source builds not allowed. Please make a new directory (called a build directory) and run CMake from there."
  )
endif()

add_subdirectory(engine)