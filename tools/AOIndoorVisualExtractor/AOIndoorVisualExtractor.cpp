// Local runtime/offline visual reader. No AO client or graphics device is started.
// Native selection/deformation is version-bound; never call these offsets on another DLL.
#include <windows.h>
#include <wincrypt.h>
#include <cstdint>
#include <cmath>
#include <fstream>
#include <iostream>
#include <stdexcept>
#include <string>
#include <vector>
#include <set>
#include <limits>
#include <algorithm>

static_assert(sizeof(void*) == 4, "Build with the 32-bit MinGW compiler");
struct Identity { int type, instance; };
using Constructor = void* (__thiscall*)(void*);
using ReadDbObject = void* (__thiscall*)(void*, const Identity&);
using Open = int (__thiscall*)(void*, void*, bool);
using ConstructString = void* (__thiscall*)(void*, const char*, int);
using ConstructTilemap = void* (__thiscall*)(void*, int, void*);
using Variant = int (__thiscall*)(void*, int, int, int, int*);
using Emit = void (__thiscall*)(void*, int*, void*, void*, uint32_t*, float, float, float, int*, int, void*);
struct Room { int index, rotation, x1, y1, x2, y2; float x, y, z, centerX, centerZ; };
struct Vertex { float position[3], normal[3]; uint32_t color; float uv[2]; };
static_assert(sizeof(Vertex) == 36, "Unexpected visual vertex layout");

template<class T> T Read(std::ifstream& input) {
    T result; input.read(reinterpret_cast<char*>(&result), sizeof(result));
    if (!input) throw std::runtime_error("Truncated room plan"); return result;
}
template<class T> void Write(std::ofstream& output, T value) {
    output.write(reinterpret_cast<char*>(&value), sizeof(value));
}
template<class T> T Field(void* object, size_t offset) {
    return *reinterpret_cast<T*>(static_cast<uint8_t*>(object) + offset);
}
template<class T> T Export(HMODULE module, const char* name) {
    auto address = GetProcAddress(module, name);
    if (!address) throw std::runtime_error(std::string("Missing export: ") + name);
    return reinterpret_cast<T>(address);
}
static std::string Sha256(const std::string& path) {
    HCRYPTPROV provider=0; HCRYPTHASH hash=0;
    if (!CryptAcquireContextA(&provider,nullptr,nullptr,PROV_RSA_AES,CRYPT_VERIFYCONTEXT)
        || !CryptCreateHash(provider,CALG_SHA_256,0,0,&hash)) throw std::runtime_error("SHA256 initialization failed");
    std::ifstream input(path,std::ios::binary); if(!input) throw std::runtime_error("Cannot read N3.dll");
    char buffer[65536]; while(input) { input.read(buffer,sizeof(buffer));
        if(!CryptHashData(hash,reinterpret_cast<BYTE*>(buffer),static_cast<DWORD>(input.gcount()),0)) throw std::runtime_error("SHA256 failed"); }
    BYTE digest[32]; DWORD length=32; CryptGetHashParam(hash,HP_HASHVAL,digest,&length,0);
    CryptDestroyHash(hash); CryptReleaseContext(provider,0);
    const char* hex="0123456789abcdef"; std::string result;
    for(auto byte:digest) { result+=hex[byte>>4]; result+=hex[byte&15]; } return result;
}
static void Rotate(float& x, float& z, int turns) {
    float oldX=x; switch(turns&3) { case 1:x=z;z=-oldX;break;case 2:x=-x;z=-z;break;case 3:x=-z;z=oldX;break; }
}
static int VertexBudget(void* visual, bool floorOnly=false) {
    if(!visual) return 0; int parts=Field<int>(visual,0x1c);
    if(parts<0||parts>64) throw std::runtime_error("Invalid visual part count");
    auto array=Field<uint8_t*>(visual,0x20); int total=0;
    for(int i=0;i<parts&&(!floorOnly||i==0);++i) {
        int count=Field<int>(array+i*16,0);
        if(count<0||count>32768) throw std::runtime_error("Invalid visual vertex count"); total+=count;
    } return total;
}
// MSVC 2010 map<uint32, vector<uint16>> nodes; this ABI is covered by the DLL hash.
static void CollectNodes(uint8_t* node, uint8_t* head, std::vector<uint8_t*>& nodes) {
    if(node==head) return;
    if(!node||nodes.size()>64) throw std::runtime_error("Invalid material tree");
    CollectNodes(Field<uint8_t*>(node,0),head,nodes); nodes.push_back(node);
    CollectNodes(Field<uint8_t*>(node,8),head,nodes);
}
int main(int argc,char** argv) try {
    if(argc!=4) { std::cerr<<"Usage: AOIndoorVisualExtractor <AO-install> <room-plan> <output.aovr>\n"; return 2; }
    std::string root=argv[1];
    // Read-only AO database loading and all private functions/struct layouts are tied to this build.
    if(Sha256(root+"\\N3.dll")!="8c019efd72d547879a06585b69147ab1546b9617a2fce090e5863791aec8b0bb")
        throw std::runtime_error("Unsupported N3.dll build; no native offsets were called");
    std::ifstream plan(argv[2],std::ios::binary); char magic[4];plan.read(magic,4);
    if(std::string(magic,4)!="AORP"||Read<int>(plan)!=1) throw std::runtime_error("Invalid room plan");
    int pf=Read<int>(plan),tilemapId=Read<int>(plan),roomCount=Read<int>(plan);
    if(pf<=0||tilemapId<=0||roomCount<1||roomCount>65535) throw std::runtime_error("Invalid room plan metadata");
    std::vector<Room> rooms(roomCount);for(auto& room:rooms)room=Read<Room>(plan);
    SetCurrentDirectoryA(root.c_str());auto n3=LoadLibraryA("N3.dll");LoadLibraryA("Gamecode.dll");LoadLibraryA("Interfaces.dll");
    auto dc=LoadLibraryA("DatabaseController.dll"),utils=LoadLibraryA("Utils.dll");
    if(!n3||!dc||!utils) throw std::runtime_error("Failed loading database DLLs");
    std::vector<uint8_t> databaseStorage(0x200),stringStorage(0x100),tilemapStorage(0x41c);
    void* db=Export<Constructor>(dc,"??0ResourceDatabase_t@@QAE@XZ")(databaseStorage.data());
    std::string data=root+"\\cd_image\\data";
    void* str=Export<ConstructString>(utils,"??0String@@QAE@PBDH@Z")(stringStorage.data(),data.c_str(),data.size());
    if(Export<Open>(dc,"?Open@ResourceDatabase_t@@QAEHABV?$basic_string@DU?$char_traits@D@std@@V?$allocator@D@2@@std@@_N@Z")(db,str,true))
        throw std::runtime_error("Read-only resource database open failed");
    auto get=Export<ReadDbObject>(dc,"?GetDbObject@ResourceDatabase_t@@UAEPAVDbObject_t@@ABVIdentity_t@@@Z");
    void* rdb=get(db,Identity{1000009,tilemapId});if(!rdb)throw std::runtime_error("Missing tilemap");
    auto base=reinterpret_cast<uint8_t*>(n3);
    auto tm=reinterpret_cast<ConstructTilemap>(base+0x173f8)(tilemapStorage.data(),tilemapId,rdb);
    int width=Field<int>(rdb,0x825c),height=Field<int>(rdb,0x8260);
    if(width<1||height<1||width>4096||height>4096||Field<int>(rdb,0x18)!=1)throw std::runtime_error("Invalid indoor tilemap");
    auto ids=Field<uint16_t*>(tm,0x10);auto flags=Field<uint32_t*>(tm,0x14);auto types=Field<uint8_t*>(rdb,0x20);
    auto materialIds=Field<uint32_t*>(rdb,0x824c);auto cellMaterials=Field<uint8_t*>(rdb,0x8254);int channels=Field<uint16_t>(rdb,0x8250);
    auto heights=Field<uint8_t*>(rdb,0x228);float heightScale=Field<float>(rdb,0x1c);
    if(!ids||!flags||!types||!materialIds||!cellMaterials||!heights||channels<1||channels>64
        ||!std::isfinite(heightScale)||heightScale<=0)throw std::runtime_error("Incomplete tilemap channels");
    auto variant=reinterpret_cast<Variant>(base+0x16967);auto emit=reinterpret_cast<Emit>(base+0x1c3b);
    auto nativeFree=Export<void (__cdecl*)(void*)>(GetModuleHandleA("MSVCR100.dll"),"free");
    std::ofstream output(argv[3],std::ios::binary|std::ios::trunc);if(!output)throw std::runtime_error("Cannot create output");
    output.write("AOVR",4);Write(output,1);Write(output,pf);Write(output,tilemapId);Write(output,roomCount);
    int totalCells=0,totalTriangles=0; std::set<int> visualIds;
    for(auto room:rooms) {
        if(room.x1<0||room.y1<0||room.x2>width||room.y2>height||room.x2<=room.x1||room.y2<=room.y1)throw std::runtime_error("Room outside tilemap");
        // RDBPlayfield_t::CalculateRoomHeights takes the minimum height of occupied
        // cells, ignoring type zero. CreateDungeonRoom passes its negative as the
        // emitter Y offset. Empty cells must not pull a raised room's baseline to zero.
        float floor=std::numeric_limits<float>::max();
        for(int y=room.y1;y<room.y2;++y)for(int x=room.x1;x<room.x2;++x) {
            int cell=y*width+x;if(types[cell]&127)floor=std::min(floor,heights[cell]*heightScale);
        }
        if(floor==std::numeric_limits<float>::max())throw std::runtime_error("Room has no occupied heightmap cells");
        Write(output,room.index);auto countPosition=output.tellp();Write(output,0);int cellCount=0;
        for(int y=room.y1;y<room.y2;++y)for(int x=room.x1;x<room.x2;++x) {
            int cell=y*width+x;int mainId=ids[cell]&16383;if(!mainId)continue;
            uint32_t ctx[8]={static_cast<uint32_t>(x),static_cast<uint32_t>(y),reinterpret_cast<uint32_t>(tm),0,static_cast<uint32_t>(ids[cell]>>14),0,0,(flags[cell]>>24)&15};
            visualIds.insert(mainId); auto main=get(db,Identity{1010013,mainId});if(!main)throw std::runtime_error("Missing visual tile "+std::to_string(mainId));ctx[3]=reinterpret_cast<uint32_t>(main);
            int type=types[cell]&127,rot=0;
            if(Field<int>(main,0x1c)==0||Field<int>(Field<uint8_t*>(main,0x20),8)==0) {
                int id=variant(tm,type,type,0,&rot);visualIds.insert(id);ctx[5]=reinterpret_cast<uint32_t>(get(db,Identity{1010013,id}));
                if(!ctx[5])throw std::runtime_error("Missing floor fallback tile");
            }
            if(ctx[7]) {int id=variant(tm,type,0,31,&rot);visualIds.insert(id);ctx[6]=reinterpret_cast<uint32_t>(get(db,Identity{1010013,id}));if(!ctx[6])throw std::runtime_error("Missing wall overlay tile");}
            int budget=VertexBudget(main)+VertexBudget(reinterpret_cast<void*>(ctx[5]),true)+4*VertexBudget(reinterpret_cast<void*>(ctx[6]));
            if(budget<1||budget>32768)throw std::runtime_error("Tile exceeds visual export limits");
            std::vector<Vertex> vertices(budget);std::vector<uint32_t> hashes(budget);uint8_t head[0x24]={};uint8_t map[0x20]={};
            *reinterpret_cast<void**>(head)=head;*reinterpret_cast<void**>(head+4)=head;*reinterpret_cast<void**>(head+8)=head;head[0x20]=head[0x21]=1;*reinterpret_cast<void**>(map+4)=head;
            uint32_t materials[64]={};for(int c=0;c<channels;++c)materials[c]=materialIds[cellMaterials[cell*channels+c]];
            int count=0,previous[4]={};float scale=Field<float>(rdb,0x8264);
            emit(ctx,&count,vertices.data(),map,materials,(x-room.x1-room.centerX)*scale,-floor,(y-room.y1-room.centerZ)*scale,previous,0,hashes.data());
            if(count<0||count>budget)throw std::runtime_error("Invalid emitted vertex count");
            std::vector<uint8_t*> nodes;CollectNodes(Field<uint8_t*>(head,4),head,nodes);
            Write(output,x);Write(output,y);Write(output,mainId);Write(output,static_cast<int>(ids[cell]>>14));Write(output,count);
            for(int i=0;i<count;++i) {auto v=vertices[i];Rotate(v.position[0],v.position[2],room.rotation);Rotate(v.normal[0],v.normal[2],room.rotation);v.position[0]+=room.x;v.position[1]+=room.y;v.position[2]+=room.z;
                for(float f:v.position)if(!std::isfinite(f))throw std::runtime_error("Nonfinite position");for(float f:v.normal)if(!std::isfinite(f))throw std::runtime_error("Nonfinite normal");for(float f:v.uv)if(!std::isfinite(f))throw std::runtime_error("Nonfinite UV");Write(output,v); }
            Write(output,static_cast<int>(nodes.size()));
            for(auto node:nodes) {Write(output,Field<uint32_t>(node,0xc));auto begin=Field<uint16_t*>(node,0x10),end=Field<uint16_t*>(node,0x14);int indices=end-begin;
                if(indices<0||indices>196608||indices%3)throw std::runtime_error("Invalid triangle count");Write(output,indices);
                for(int i=0;i<indices;++i){if(begin[i]>=count)throw std::runtime_error("Visual index outside vertex buffer");Write(output,begin[i]);}totalTriangles+=indices/3; }
            // These buffers/nodes were allocated by AO's CRT, not this executable's CRT.
            for(auto node:nodes) {nativeFree(Field<void*>(node,0x10));nativeFree(node);}
            ++cellCount;
        }
        auto end=output.tellp();output.seekp(countPosition);Write(output,cellCount);output.seekp(end);totalCells+=cellCount;
        std::cout<<"Room "<<room.index<<": "<<cellCount<<" visual cells, source height baseline "<<floor<<"\n";
    }
    output.write("AOVD",4);Write(output,static_cast<int>(visualIds.size()));for(int id:visualIds)Write(output,id);
    output.close();if(!output)throw std::runtime_error("Visual output write failed");
    std::cout<<"PF "<<pf<<": "<<totalCells<<" visual cells, "<<totalTriangles<<" triangles\n";return 0;
} catch(const std::exception& error) {std::cerr<<error.what()<<"\n";return 1;}
