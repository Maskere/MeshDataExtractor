using SharpGLTF.Memory;
using SharpGLTF.Schema2;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;

namespace MeshDataExtract{
    public class MeshDataExtractor{
        static Stopwatch sw = new();

        public void GlftDataExtractor(string? filePath, out List<float> finalVertices, out List<uint> finalIndices, out List<string> textureFilePaths){
            finalVertices = new();
            finalIndices = new();
            textureFilePaths = new();
            ModelRoot model = ModelRoot.Load(filePath);

            Mesh mesh = model.LogicalMeshes[0];
            MeshPrimitive primitive = mesh.Primitives[0];

            finalIndices = primitive.GetIndices().Select(i => (uint)i).ToList();

            Accessor positionAccessor = primitive.GetVertexAccessor("POSITION");
            Accessor uvsAccessor = primitive.GetVertexAccessor("TEXCOORD_0");
            Accessor normalsAccessor = primitive.GetVertexAccessor("NORMAL");
            Accessor? jointAccessor = primitive.GetVertexAccessor("JOINTS_0");
            Accessor? weightsAccessor = primitive.GetVertexAccessor("WEIGHTS_0");

            Vector3[] positions = positionAccessor.AsVector3Array().ToArray();
            Vector2[] uvs = uvsAccessor.AsVector2Array().ToArray();
            Vector3[] normals = normalsAccessor.AsVector3Array().ToArray();
            Vector4[] joints = jointAccessor?.AsVector4Array().ToArray() ?? Array.Empty<Vector4>();
            Vector4[] weights = weightsAccessor?.AsVector4Array().ToArray() ?? Array.Empty<Vector4>();
            
            int vertexCount = positions.Length;

            for(int i = 0; i < vertexCount; i++){
                finalVertices.Add(positions[i].X);
                finalVertices.Add(positions[i].Y);
                finalVertices.Add(positions[i].Z);

                if(uvs.Length > i){
                    finalVertices.Add(uvs[i].X);
                    finalVertices.Add(uvs[i].Y);
                }
                else{
                    finalVertices.Add(0f);
                    finalVertices.Add(0f);
                }

                if(normals.Length > i){
                    finalVertices.Add(normals[i].X);
                    finalVertices.Add(normals[i].Y);
                    finalVertices.Add(normals[i].Z);
                }
                else{
                    finalVertices.Add(0);
                    finalVertices.Add(0);
                    finalVertices.Add(0);
                }

                if(joints.Length > i){
                    finalVertices.Add(joints[i].X);
                    finalVertices.Add(joints[i].Y);
                    finalVertices.Add(joints[i].Z);
                    finalVertices.Add(joints[i].W);
                }
                else{
                    finalVertices.Add(0);
                    finalVertices.Add(0);
                    finalVertices.Add(0);
                    finalVertices.Add(0);
                }
                
                if(weights.Length > i){
                    finalVertices.Add(weights[i].X);
                    finalVertices.Add(weights[i].Y);
                    finalVertices.Add(weights[i].Z);
                    finalVertices.Add(weights[i].W);
                }
                else{
                    finalVertices.Add(0);
                    finalVertices.Add(0);
                    finalVertices.Add(0);
                    finalVertices.Add(0);
                }
            }

            if(primitive.Material == null) return;

            Material material = primitive.Material;
            var pbr = material.WithPBRMetallicRoughness();
            Texture? gltfTexture = pbr.GetDiffuseTexture();

            if(gltfTexture != null){
                Image? image = gltfTexture.PrimaryImage;

                if(image != null){
                    string uniqueKey = image.AlternateWriteFileName ?? image.Name ?? $"glTF_Texture_{gltfTexture.LogicalIndex}";

                    textureFilePaths.Add(uniqueKey);
                }
            }
        }

        public void FBXDataExtractor(string? filePath, out List<float> finalVertices, out List<uint> finalIndices){
            if (filePath == null) throw new ArgumentException("Invalid file path.");
            finalVertices = new();
            finalIndices = new();

            ReadOnlySpan<char> data = File.ReadAllText(filePath);
            int pos = 0;

            while(pos < data.Length){
                int next = data[pos..].IndexOf('\n');
                ReadOnlySpan<char> line = (next == -1) ? data[pos..] : data[pos..(pos + next)];
                pos += (next == -1) ? data.Length : next + 1;

                if(line[0] == ';') continue;

                Console.WriteLine(line[0]);
            }
        }

        public void WavefrontObjDataExtractor(string? filePath, out List<float> finalVertices, out List<uint> finalIndices){
            if (filePath == null) throw new ArgumentException("Invalid file path.");
            finalVertices = new();
            finalIndices = new();

            Dictionary<string, uint> vertexMap = new();
            uint currentIndex = 0; 

            //HUSK RÆKKEFØLGEN HER
            List<Vector3> rawPositions = new();
            List<Vector2> rawUVs = new();
            List<Vector3> rawNormals = new();

            List<string> faceRefs = new(); 

            ReadOnlySpan<char> data = File.ReadAllText(filePath);
            int pos = 0;
            while(pos < data.Length){
                int next = data[pos..].IndexOf('\n');
                ReadOnlySpan<char> line = (next == -1) ? data[pos..] : data[pos..(pos + next)];
                pos += (next == -1) ? data.Length : next + 1;

                if(line.Length == 0 || line[0] == '#') continue;

                if(line.StartsWith("v ",StringComparison.Ordinal)){
                    rawPositions.Add(ParseVertex(line[2..]));
                }
                else if(line.StartsWith("vt ",StringComparison.Ordinal)){
                    rawUVs.Add(ParseUV(line[3..]));
                }
                else if(line.StartsWith("vn ",StringComparison.Ordinal)){
                    rawNormals.Add(ParseNormal(line[3..]));
                }
                else if(line.StartsWith("f ",StringComparison.Ordinal)){
                    ParseFace(line[2..],faceRefs);
                }
            }

            foreach (string refString in faceRefs) {
                // 1. Check if this exact combination (v/vt/vn) has been seen before
                if (vertexMap.TryGetValue(refString, out uint existingIndex)) {
                    // Combination already exists: Add its existing index to the EBO.
                    finalIndices.Add(existingIndex);
                }
                else {
                    // Combination is NEW: Create a new combined vertex, add its index to the EBO, and map it.

                    // a. Add the current index to the EBO list
                    finalIndices.Add(currentIndex);

                    // b. Map the combination string to the new index
                    vertexMap.Add(refString, currentIndex);

                    // c. Parse the v/vt/vn reference string (e.g., "2/1/1")
                    string[] indices = refString.Split('/');

                    // OBJ indices are 1-based, so subtract 1 for 0-based C# lists
                    int vIndex = (indices.Length > 0 && !string.IsNullOrEmpty(indices[0])) ? int.Parse(indices[0]) - 1 : -1;
                    int vtIndex = (indices.Length > 0 && !string.IsNullOrEmpty(indices[1])) ? int.Parse(indices[1]) - 1 : -1;
                    int vnIndex = (indices.Length > 0 && !string.IsNullOrEmpty(indices[2])) ? int.Parse(indices[2]) - 1 : -1;

                    // d. Retrieve the data from the raw lists and append to the VBO stream (finalVertices)

                    if(vIndex >= 0 && vIndex < rawPositions.Count){
                        // Position (3 floats)
                        finalVertices.Add(rawPositions[vIndex].X);
                        finalVertices.Add(rawPositions[vIndex].Y);
                        finalVertices.Add(rawPositions[vIndex].Z);
                    }
                    else{
                        finalVertices.Add(0);
                        finalVertices.Add(0);
                        finalVertices.Add(0);
                    }

                    if(vtIndex >= 0 && vtIndex < rawUVs.Count){
                        // UV (2 floats)
                        finalVertices.Add(rawUVs[vtIndex].X); // U component
                        finalVertices.Add(rawUVs[vtIndex].Y); // V component
                    }
                    else{
                        finalVertices.Add(0);
                        finalVertices.Add(0);
                    }

                    if(vnIndex >= 0 && vnIndex < rawNormals.Count){
                        // Normal (3 floats)
                        finalVertices.Add(rawNormals[vnIndex].X);
                        finalVertices.Add(rawNormals[vnIndex].Y);
                        finalVertices.Add(rawNormals[vnIndex].Z);
                    }
                    else{
                        finalVertices.Add(0);
                        finalVertices.Add(0);
                        finalVertices.Add(0);
                    }

                    // e. Increment the index counter for the next new vertex
                    currentIndex++;
                }
            }
        }

        public void PlyExtractData(string? filePath,out int stride, out List<float> vertices, out List<uint> indices){
            if(filePath == null) throw new ArgumentNullException("Invalid file");

            List<string> header = new();
            List<string> content = new();
            List<string> vertexLines = new();
            List<string> faceLines = new();

            using(StreamReader reader = new(filePath)){
                while(!reader.EndOfStream){
                    string? c = reader.ReadLine();
                    if(c == "end_header") break;
                    if(c != null){
                        header.Add(c);
                    }
                }
                while(!reader.EndOfStream){
                    string? c = reader.ReadLine();
                    if(c != null){
                        content.Add(c);
                    }
                }
            }

            string? getN = header.Find(header => header.Contains("element vertex"));
            string[] getNSplit = getN != null ? getN.Split(" ") : new string[0];
            int.TryParse(getNSplit[2],out int numberOfVerticies);

            string? getFaceN = header.Find(header => header.Contains("element face"));
            string[] getFaceNSplit = getFaceN != null ? getFaceN.Split(" ") : new string[0];
            int.TryParse(getFaceNSplit[2],out int numberOfFaces);

            for(int i = 0; i < numberOfVerticies && i < content.Count; i++){
                vertexLines.Add(content[i]);
            }
            for(int i = numberOfVerticies; i < content.Count; i++){
                faceLines.Add(content[i]);
            }

            if(content.Count > 0){
                stride = content[0].Split(" ", StringSplitOptions.RemoveEmptyEntries).Length;
            }
            else{
                stride = 0;
            }

            vertices = new();
            foreach(string line in vertexLines){
                string[] spl = line.Split(" ",StringSplitOptions.RemoveEmptyEntries);
                for(int l = 0; l < spl.Length; l++){
                    if(float.TryParse(spl[l],NumberStyles.Float,CultureInfo.InvariantCulture, out float fRes))
                        vertices.Add(fRes);
                }
            }

            int faceStartIndex = numberOfVerticies;
            int faceEndIndex = numberOfVerticies + numberOfFaces;

            indices = new();
            foreach(string line in faceLines){
                string[] spl = line.Split(" ",StringSplitOptions.RemoveEmptyEntries);
                if(spl.Length < 3) continue;

                // int vertsInFace = int.Parse(spl[0]);
                if(!int.TryParse(spl[0],out int vertsInFace)) continue;

                List<uint> faceVerts = new();

                for(int a = 1; a <= vertsInFace && a < spl.Length; a++){
                    if(uint.TryParse(spl[a],out uint fRes))
                        faceVerts.Add(fRes);
                }

                for(int i = 1; i < faceVerts.Count - 1; i++){
                    indices.Add(faceVerts[0]);
                    indices.Add(faceVerts[i]);
                    indices.Add(faceVerts[i + 1]);
                }
            }
        }

        static Vector3 ParseVertex(ReadOnlySpan<char> line){
            float x = ParseNextFloat(ref line);
            float y = ParseNextFloat(ref line);
            float z = ParseNextFloat(ref line);
            return new Vector3(x,y,z);
        }

        static Vector2 ParseUV(ReadOnlySpan<char> line){
            float x = ParseNextFloat(ref line);
            float y = ParseNextFloat(ref line);
            return new Vector2(x,y);
        }

        static Vector3 ParseNormal(ReadOnlySpan<char> line){
            float x = ParseNextFloat(ref line);
            float y = ParseNextFloat(ref line);
            float z = ParseNextFloat(ref line);
            return new Vector3(x,y,z);
        }

        static void ParseFace(ReadOnlySpan<char> line,List<string> faceRefs){
            while(!line.IsEmpty){
                int space = line.IndexOf(' ');
                ReadOnlySpan<char> token = (space == -1) ? line : line[..space];
                line = (space == -1) ? ReadOnlySpan<char>.Empty : line[(space + 1)..];

                if(token.Length > 0)
                    faceRefs.Add(token.ToString());
            }
        }

        static float ParseNextFloat(ref ReadOnlySpan<char> line){
            int space = line.IndexOf(' ');
            ReadOnlySpan<char> token = (space == -1) ? line : line[..space];
            line = (space == -1) ? ReadOnlySpan<char>.Empty : line[(space + 1)..];

            return float.TryParse(token,NumberStyles.Float,CultureInfo.InvariantCulture, out float val) ? val : 0f;
        }
    }
}
