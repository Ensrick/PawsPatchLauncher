// Same contract as game/beta7/TerrainRuntime.cs; the isolated rig deliberately
// does not link the complete game helper or install any other crash fixes.
internal interface IMemory
{
    byte[] Read(uint address,int count);
    void Write(uint address,byte[] bytes);
    void WriteCode(uint address,byte[] bytes);
    uint Allocate(int count);
    void MakeExecutable(uint address,int count);
    void Flush(uint address,int count);
    void Free(uint address);
}
