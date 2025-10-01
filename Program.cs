namespace MeshDataExtract{
    public class Program{
        static void Main(string[] args){
            string? filePath = "";

            MeshDataExtractor ext = new();
            ext.WavefrontObjDataExtractor(filePath,out List<float> vertices,out List<uint> indices);
            foreach(uint v in indices){
                Console.WriteLine(v);
            }
        }
    }
}
