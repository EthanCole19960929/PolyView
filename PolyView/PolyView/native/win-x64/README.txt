draco_decoder.dll —— Google Draco 解码库（Apache License 2.0，见 draco_LICENSE.txt）

来源：https://github.com/google/draco  标签 1.5.7（提交 8786740086a9f4d83f44aa83badfbea4dce7a1b5）
目标：DRACO_UNITY_PLUGIN 生成的 dracodec_unity（C 接口见 src/draco/unity/draco_unity_plugin.h），改名为 draco_decoder.dll
编译：Debian + mingw-w64 12（x86_64-w64-mingw32-g++-posix）交叉编译，静态链接 libgcc / libstdc++，只依赖 KERNEL32.dll 和 msvcrt.dll
  cmake .. -G Ninja -DCMAKE_TOOLCHAIN_FILE=mingw.cmake -DCMAKE_BUILD_TYPE=Release -DDRACO_UNITY_PLUGIN=ON -DDRACO_TESTS=OFF \
        -DCMAKE_MODULE_LINKER_FLAGS="-static -static-libgcc -static-libstdc++ -s"
  ninja dracodec_unity
SHA-256：f5630f2cf3a10bfe9630c1983f8e6d8bf67d67f9e008279b4a875031f3450cbc
用途：DracoGlb.cs 解开 GLB 里的 KHR_draco_mesh_compression。
