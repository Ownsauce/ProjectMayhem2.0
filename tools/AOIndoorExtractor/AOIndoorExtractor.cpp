#include <windows.h>
#include <cstdint>
#include <fstream>
#include <iostream>
#include <string>
#include <vector>

#pragma pack(push, 1)
struct DBIdentity {
    std::int32_t type;
    std::int32_t instance;
};

struct SurfaceMeshData {
    std::int32_t triangleCount;
    std::int32_t vertexCount;
    std::uint8_t* triangleData;
    std::int32_t fields[11];
};
#pragma pack(pop)

static_assert(sizeof(SurfaceMeshData) == 0x38, "Unexpected 32-bit surface mesh layout");

using ConstructorFn = void* (__thiscall*)(void*);
using OpenFn = int (__thiscall*)(void*, void*, bool);
using GetDbObjectFn = void* (__thiscall*)(void*, const DBIdentity&);
using StringConstructorFn = void* (__thiscall*)(void*, const char*, int);

template <typename T>
static void Write(std::ofstream& output, const T& value) {
    output.write(reinterpret_cast<const char*>(&value), sizeof(T));
}

static FARPROC RequireExport(HMODULE module, const char* name) {
    FARPROC result = GetProcAddress(module, name);
    if (result == nullptr) {
        std::cerr << "Missing AO DLL export: " << name << "\n";
        std::exit(3);
    }
    return result;
}

int main(int argc, char** argv) {
    if (argc != 6) {
        std::cerr << "Usage: AOIndoorExtractor <ao-path> <playfield-id> <tilemap-id> <room-count> <output-file>\n";
        return 2;
    }

    std::string aoPath = argv[1];
    int playfieldId = std::stoi(argv[2]);
    int tilemapId = std::stoi(argv[3]);
    int roomCount = std::stoi(argv[4]);
    std::string outputPath = argv[5];
    if (playfieldId <= 0 || tilemapId <= 0 || roomCount <= 0 || roomCount > 65535) {
        std::cerr << "Invalid playfield ID or room count.\n";
        return 2;
    }

    SetCurrentDirectoryA(aoPath.c_str());
    LoadLibraryA("N3.dll");
    LoadLibraryA("Gamecode.dll");
    LoadLibraryA("Interfaces.dll");
    HMODULE databaseController = LoadLibraryA("DatabaseController.dll");
    HMODULE utils = LoadLibraryA("Utils.dll");
    if (databaseController == nullptr || utils == nullptr) {
        std::cerr << "Failed loading AO database runtime DLLs (error " << GetLastError() << ").\n";
        return 3;
    }

    auto constructDatabase = reinterpret_cast<ConstructorFn>(RequireExport(
        databaseController, "??0ResourceDatabase_t@@QAE@XZ"));
    auto openDatabase = reinterpret_cast<OpenFn>(RequireExport(
        databaseController,
        "?Open@ResourceDatabase_t@@QAEHABV?$basic_string@DU?$char_traits@D@std@@V?$allocator@D@2@@std@@_N@Z"));
    auto getDbObject = reinterpret_cast<GetDbObjectFn>(RequireExport(
        databaseController,
        "?GetDbObject@ResourceDatabase_t@@UAEPAVDbObject_t@@ABVIdentity_t@@@Z"));
    auto constructString = reinterpret_cast<StringConstructorFn>(RequireExport(
        utils, "??0String@@QAE@PBDH@Z"));

    std::vector<std::uint8_t> databaseStorage(0x200);
    std::vector<std::uint8_t> stringStorage(0x100);
    void* database = constructDatabase(databaseStorage.data());
    std::string databasePath = aoPath + "\\cd_image\\data";
    void* aoDatabasePath = constructString(
        stringStorage.data(), databasePath.c_str(), static_cast<int>(databasePath.size()));
    int openResult = openDatabase(database, aoDatabasePath, true);
    if (openResult != 0) {
        std::cerr << "AO ResourceDatabase open failed with code " << openResult << ".\n";
        return 4;
    }

    std::ofstream output(outputPath, std::ios::binary | std::ios::trunc);
    if (!output) {
        std::cerr << "Could not create output file.\n";
        return 5;
    }

    output.write("AOIS", 4);
    std::int32_t version = 2;
    Write(output, version);
    Write(output, playfieldId);
    std::streampos roomCountPosition = output.tellp();
    std::int32_t writtenRooms = 0;
    Write(output, writtenRooms);

    for (int roomInstance = 0; roomInstance < roomCount; ++roomInstance) {
        DBIdentity identity{1000013, (playfieldId << 16) | roomInstance};
        auto* object = static_cast<std::uint8_t*>(getDbObject(database, identity));
        if (object == nullptr)
            continue;

        auto* surface = object + 0x18;
        auto** meshArrayPointer = reinterpret_cast<SurfaceMeshData**>(surface + 0x0c);
        auto* meshArray = *meshArrayPointer;
        int meshCount = *reinterpret_cast<std::int32_t*>(surface + 0x10);
        if (meshArray == nullptr || meshCount <= 0 || meshCount > 100000)
            continue;

        Write(output, roomInstance);
        Write(output, meshCount);
        int validMeshes = 0;
        std::streampos meshCountPosition = output.tellp();
        output.seekp(-static_cast<std::streamoff>(sizeof(std::int32_t)), std::ios::cur);
        Write(output, validMeshes);

        for (int meshIndex = 0; meshIndex < meshCount; ++meshIndex) {
            const SurfaceMeshData& mesh = meshArray[meshIndex];
            if (mesh.vertexCount <= 0 || mesh.vertexCount > 200000
                || mesh.triangleCount <= 0 || mesh.triangleCount > 400000
                || mesh.triangleData == nullptr) {
                continue;
            }

            Write(output, mesh.vertexCount);
            Write(output, mesh.triangleCount);
            output.write(reinterpret_cast<const char*>(mesh.fields), sizeof(mesh.fields));

            int indexWidth = mesh.vertexCount > 255 ? 2 : 1;
            int triangleBytes = mesh.triangleCount * 3 * indexWidth;
            int alignedTriangleBytes = (triangleBytes + 3) & ~3;
            auto* vertices = mesh.triangleData + alignedTriangleBytes;
            output.write(reinterpret_cast<const char*>(vertices), mesh.vertexCount * 12);

            for (int index = 0; index < mesh.triangleCount * 3; ++index) {
                std::int32_t converted = indexWidth == 1
                    ? mesh.triangleData[index]
                    : reinterpret_cast<const std::int16_t*>(mesh.triangleData)[index];
                Write(output, converted);
            }
            ++validMeshes;
        }

        std::streampos endPosition = output.tellp();
        output.seekp(meshCountPosition - static_cast<std::streamoff>(sizeof(std::int32_t)));
        Write(output, validMeshes);
        output.seekp(endPosition);
        ++writtenRooms;
    }

    std::streampos geometryEnd = output.tellp();
    output.seekp(roomCountPosition);
    Write(output, writtenRooms);
    output.seekp(geometryEnd);

    DBIdentity tilemapIdentity{1000009, tilemapId};
    auto* tilemap = static_cast<std::uint8_t*>(getDbObject(database, tilemapIdentity));
    if (tilemap == nullptr) {
        std::cerr << "AO dungeon tilemap was not materialized.\n";
        return 7;
    }
    bool isDungeon = *reinterpret_cast<bool*>(tilemap + 0x18);
    float heightmapScale = *reinterpret_cast<float*>(tilemap + 0x1c);
    auto* collisionData = *reinterpret_cast<std::uint8_t**>(tilemap + 0x20);
    auto* heightmapData = *reinterpret_cast<std::uint8_t**>(tilemap + 0x228);
    int width = *reinterpret_cast<std::int32_t*>(tilemap + 0x825c);
    int height = *reinterpret_cast<std::int32_t*>(tilemap + 0x8260);
    float tileSize = *reinterpret_cast<float*>(tilemap + 0x8264);
    std::int64_t tileCount = static_cast<std::int64_t>(width) * height;
    if (!isDungeon || width <= 0 || height <= 0 || width > 32768 || height > 32768
        || tileCount > 268435456 || collisionData == nullptr || heightmapData == nullptr
        || heightmapScale <= 0 || tileSize <= 0) {
        std::cerr << "AO dungeon tilemap layout is invalid.\n";
        return 8;
    }
    output.write("AOTM", 4);
    Write(output, tilemapId);
    Write(output, width);
    Write(output, height);
    Write(output, heightmapScale);
    Write(output, tileSize);
    output.write(reinterpret_cast<const char*>(heightmapData), tileCount);
    output.write(reinterpret_cast<const char*>(collisionData), tileCount);
    output.close();
    std::cout << "Extracted " << writtenRooms << " rooms for playfield "
              << playfieldId << " to " << outputPath << "\n";
    return writtenRooms > 0 ? 0 : 6;
}
