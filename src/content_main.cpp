#include "ContentPackage.h"
#include "Chapter.h"

#include <yorehold/framework/assets/FileSystem.h>

#include <cstdio>
#include <filesystem>
#include <string>

int main(int argc, char** argv)
{
    if (argc < 3 || (std::string(argv[1]) != "check" && std::string(argv[1]) != "pack")
        || (std::string(argv[1]) == "check" && argc > 4) || (std::string(argv[1]) == "pack" && argc != 4))
    {
        std::fprintf(stderr, "Usage: yorehold-content check <folder-or.yore> [chapter-folder]\n"
            "       yorehold-content pack <folder> <output.yore>\n");
        return 2;
    }
    const bool pack = std::string(argv[1]) == "pack";
    const std::string source = argv[2];
    yh::FileSystem files;
    // Deliberately no built-in content mount: a transferable package must contain its dependencies.
    std::string error;
    if (!ContentPackage::mount(files, source, "content"))
        error = "couldn't open " + source;
    else if (!pack && argc == 4)
    {
        const auto chapter = Chapter::load(files, argv[3], &error);
        if (!chapter && error.empty()) error = "chapter validation failed";
    }
    else
    {
        const auto content = ContentPackage::load(files, &error);
        if (content) content->validate(files, &error);
    }
    if (!error.empty())
    {
        std::fprintf(stderr, "%s\n", error.c_str());
        return 1;
    }
    if (pack)
    {
        namespace fs = std::filesystem;
        std::error_code problem;
        const fs::path root = fs::weakly_canonical(source, problem);
        const fs::path output = fs::weakly_canonical(argv[3], problem);
        const auto relative = output.lexically_relative(root);
        if (problem || !fs::is_directory(root) || fs::exists(output)
            || (!relative.empty() && !relative.is_absolute() && *relative.begin() != ".."))
        {
            std::fprintf(stderr, "Output must be a new file outside the content folder.\n");
            return 1;
        }
        if (!yh::FileSystem::packFolder(source, argv[3]))
        {
            std::fprintf(stderr, "Couldn't write %s\n", argv[3]);
            return 1;
        }
        std::printf("Packed %s\n", argv[3]);
    }
    else
        std::printf("Content valid: %s\n", source.c_str());
    return 0;
}
