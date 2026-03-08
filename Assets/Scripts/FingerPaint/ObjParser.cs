using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace FingerPaint
{
    /// <summary>
    /// Parses OBJ files produced by <see cref="MeshExporter"/> back into Unity Meshes.
    /// Reverses the X-flip and winding-order swap applied during export.
    /// </summary>
    public static class ObjParser
    {
        /// <summary>
        /// Parses an OBJ-format string into a Unity Mesh.
        /// Handles v (vertex), vn (normal), and f (face) lines.
        /// </summary>
        public static Mesh Parse(string objText)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var triIndices = new List<int>();    // vertex indices
            var triNormIndices = new List<int>(); // normal indices

            var lines = objText.Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrEmpty(line) || line[0] == '#' || line[0] == 'o')
                    continue;

                if (line.StartsWith("v "))
                {
                    var v = ParseVector3(line, 2);
                    // Reverse X-flip done by MeshExporter
                    v.x = -v.x;
                    vertices.Add(v);
                }
                else if (line.StartsWith("vn "))
                {
                    var n = ParseVector3(line, 3);
                    n.x = -n.x;
                    normals.Add(n);
                }
                else if (line.StartsWith("f "))
                {
                    // Format: "f a//a c//c b//b" (MeshExporter reverses winding: a c b)
                    // We reverse it back to: a b c
                    ParseFace(line, triIndices, triNormIndices);
                }
            }

            // Build mesh
            var mesh = new Mesh();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            // If normals are indexed differently from vertices, we need to
            // expand into a flat vertex/normal array per-triangle-vertex
            if (normals.Count > 0 && triNormIndices.Count == triIndices.Count)
            {
                var expandedVerts = new Vector3[triIndices.Count];
                var expandedNorms = new Vector3[triIndices.Count];
                var expandedTris = new int[triIndices.Count];

                for (int i = 0; i < triIndices.Count; i++)
                {
                    expandedVerts[i] = vertices[triIndices[i]];
                    expandedNorms[i] = normals[triNormIndices[i]];
                    expandedTris[i] = i;
                }

                mesh.vertices = expandedVerts;
                mesh.normals = expandedNorms;
                mesh.triangles = expandedTris;
            }
            else
            {
                mesh.vertices = vertices.ToArray();
                mesh.triangles = triIndices.ToArray();
                mesh.RecalculateNormals();
            }

            mesh.RecalculateBounds();
            return mesh;
        }

        private static Vector3 ParseVector3(string line, int startOffset)
        {
            var parts = line.Substring(startOffset).Trim().Split(' ');
            return new Vector3(
                float.Parse(parts[0], CultureInfo.InvariantCulture),
                float.Parse(parts[1], CultureInfo.InvariantCulture),
                float.Parse(parts[2], CultureInfo.InvariantCulture)
            );
        }

        private static void ParseFace(string line, List<int> triIndices, List<int> normIndices)
        {
            // "f a//na c//nc b//nb" — MeshExporter outputs reversed winding (a, c, b)
            // We swap back to (a, b, c) = indices 0, 2, 1
            var parts = line.Substring(2).Trim().Split(' ');
            if (parts.Length < 3) return;

            int v0, v1, v2;
            int n0, n1, n2;

            ParseFaceVertex(parts[0], out v0, out n0);
            ParseFaceVertex(parts[1], out v1, out n1);
            ParseFaceVertex(parts[2], out v2, out n2);

            // Reverse the winding swap: a c b → a b c
            triIndices.Add(v0);
            triIndices.Add(v2);
            triIndices.Add(v1);

            if (n0 >= 0)
            {
                normIndices.Add(n0);
                normIndices.Add(n2);
                normIndices.Add(n1);
            }
        }

        private static void ParseFaceVertex(string token, out int vertIdx, out int normIdx)
        {
            // Formats: "v//vn" or "v/vt/vn" or "v"
            vertIdx = -1;
            normIdx = -1;

            var parts = token.Split('/');
            if (parts.Length >= 1 && parts[0].Length > 0)
                vertIdx = int.Parse(parts[0]) - 1; // OBJ is 1-indexed

            if (parts.Length >= 3 && parts[2].Length > 0)
                normIdx = int.Parse(parts[2]) - 1;
        }
    }
}
