using System.Globalization;
using System.Numerics;

namespace MeshDataExtract{
    public class MeshDataExtractor{
        public void WavefrontObjDataExtractor(string? filePath, out List<float> finalVertices, out List<uint> finalIndices){
            if (filePath == null) throw new ArgumentException("Invalid file path.");
            finalVertices = new();
            finalIndices = new();

            Dictionary<string, uint> vertexMap = new();
            uint currentIndex = 0; 

            List<Vector3> rawPositions = new();
            List<Vector2> rawUVs = new();
            List<Vector3> rawNormals = new();

            List<string> faceRefs = new(); 

            try {
                foreach (string line in File.ReadLines(filePath)) {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;

                    string[] tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (tokens.Length == 0) continue;

                    string type = tokens[0].ToLowerInvariant();

                    Func<int, float> ParseFloat = (index) => {
                        if (index >= tokens.Length || 
                                !float.TryParse(tokens[index], NumberStyles.Float, CultureInfo.InvariantCulture, out float result))
                        {
                            return 0f;
                        }
                        return result;
                    };

                    switch (type) {
                        case "v":
                            rawPositions.Add(new Vector3(ParseFloat(1), ParseFloat(2), ParseFloat(3)));
                            break;

                        case "vt":
                            rawUVs.Add(new Vector2(ParseFloat(1), ParseFloat(2)));
                            break;

                        case "vn":
                            rawNormals.Add(new Vector3(ParseFloat(1), ParseFloat(2), ParseFloat(3)));
                            break;

                        case "f":
                            for (int i = 1; i < tokens.Length; i++) {
                                if (i == 4 && tokens.Length > 4) {
                                    // If it's a quad (f v1 v2 v3 v4), we treat it as two triangles: (v1, v3, v4)
                                    // We need to add refs for v1, v3, v4 here.

                                    // To keep it simple for now, we only read triangles (3 refs):
                                    // For full compatibility, implement Fan Triangulation here.

                                    // For a quad (v1, v2, v3, v4):
                                    // Triangle 1: v1, v2, v3
                                    // Triangle 2: v1, v3, v4  <-- This second triangle needs its indices added here
                                }

                                // For a simple triangle, just add the three references:
                                if (i <= 3) faceRefs.Add(tokens[i]);
                            }
                            break;
                    }
                }
            }
            catch (Exception ex) {
                Console.WriteLine($"Error reading file: {ex.Message}");
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
                    int vIndex = int.Parse(indices[0]) - 1;
                    int vtIndex = int.Parse(indices[1]) - 1;
                    int vnIndex = int.Parse(indices[2]) - 1;

                    // d. Retrieve the data from the raw lists and append to the VBO stream (finalVertices)

                    // Position (3 floats)
                    finalVertices.Add(rawPositions[vIndex].X);
                    finalVertices.Add(rawPositions[vIndex].Y);
                    finalVertices.Add(rawPositions[vIndex].Z);

                    // UV (2 floats)
                    finalVertices.Add(rawUVs[vtIndex].X); // U component
                    finalVertices.Add(rawUVs[vtIndex].Y); // V component

                    // Normal (3 floats)
                    finalVertices.Add(rawNormals[vnIndex].X);
                    finalVertices.Add(rawNormals[vnIndex].Y);
                    finalVertices.Add(rawNormals[vnIndex].Z);

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
    }
}
