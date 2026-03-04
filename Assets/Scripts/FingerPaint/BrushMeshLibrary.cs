using UnityEngine;

namespace FingerPaint
{
    /// <summary>
    /// Generates and caches low-poly procedural meshes at startup.
    /// Provides mesh lookup by index for voice-driven shape variation.
    /// </summary>
    public class BrushMeshLibrary : MonoBehaviour
    {
        // ---- Mesh indices (public constants for readability) ----
        public const int SPHERE     = 0;
        public const int CUBE       = 1;
        public const int OCTAHEDRON = 2;
        public const int DIAMOND    = 3;

        [Header("Sphere Settings")]
        [SerializeField, Range(3, 12)] private int _sphereRings  = 6;
        [SerializeField, Range(4, 24)] private int _sphereSlices = 12;

        private Mesh[] _meshes;

        /// <summary>Number of available mesh shapes.</summary>
        public int MeshCount => _meshes != null ? _meshes.Length : 0;

        /// <summary>
        /// Returns a cached mesh by index (clamped).
        /// Index 0 = sphere, 1 = cube, 2 = octahedron, 3 = diamond.
        /// </summary>
        public Mesh GetMesh(int index)
        {
            if (_meshes == null || _meshes.Length == 0) return null;
            return _meshes[Mathf.Clamp(index, 0, _meshes.Length - 1)];
        }

        private void Awake()
        {
            _meshes = new Mesh[4];
            _meshes[SPHERE]     = GenerateUVSphere(_sphereRings, _sphereSlices);
            _meshes[CUBE]       = GenerateCube();
            _meshes[OCTAHEDRON] = GenerateOctahedron(1f);
            _meshes[DIAMOND]    = GenerateOctahedron(1.5f); // elongated Y
        }

        private void OnDestroy()
        {
            if (_meshes == null) return;
            for (int i = 0; i < _meshes.Length; i++)
            {
                if (_meshes[i] != null) Destroy(_meshes[i]);
            }
        }

        // ----------------------------------------------------------------
        //  UV Sphere
        // ----------------------------------------------------------------

        private static Mesh GenerateUVSphere(int rings, int slices)
        {
            // rings = horizontal bands, slices = vertical segments
            int vertCount = (rings - 1) * slices + 2; // +2 for poles
            var verts   = new Vector3[vertCount];
            var normals = new Vector3[vertCount];

            // North pole
            verts[0]   = Vector3.up * 0.5f;
            normals[0] = Vector3.up;

            // Rings
            int idx = 1;
            for (int ring = 1; ring < rings; ring++)
            {
                float phi = Mathf.PI * ring / rings;
                float sinPhi = Mathf.Sin(phi);
                float cosPhi = Mathf.Cos(phi);

                for (int slice = 0; slice < slices; slice++)
                {
                    float theta = 2f * Mathf.PI * slice / slices;
                    float x = sinPhi * Mathf.Cos(theta);
                    float z = sinPhi * Mathf.Sin(theta);
                    float y = cosPhi;

                    verts[idx]   = new Vector3(x, y, z) * 0.5f;
                    normals[idx] = new Vector3(x, y, z).normalized;
                    idx++;
                }
            }

            // South pole
            verts[idx]   = Vector3.down * 0.5f;
            normals[idx] = Vector3.down;

            // Triangles
            int triCount = slices * 2 + (rings - 2) * slices * 2;
            var tris = new int[triCount * 3];
            int ti = 0;

            // North cap
            for (int s = 0; s < slices; s++)
            {
                tris[ti++] = 0;
                tris[ti++] = 1 + (s + 1) % slices;
                tris[ti++] = 1 + s;
            }

            // Middle bands
            for (int r = 0; r < rings - 2; r++)
            {
                int rowStart     = 1 + r * slices;
                int nextRowStart = 1 + (r + 1) * slices;

                for (int s = 0; s < slices; s++)
                {
                    int curr = rowStart + s;
                    int next = rowStart + (s + 1) % slices;
                    int below = nextRowStart + s;
                    int belowNext = nextRowStart + (s + 1) % slices;

                    tris[ti++] = curr;
                    tris[ti++] = next;
                    tris[ti++] = belowNext;

                    tris[ti++] = curr;
                    tris[ti++] = belowNext;
                    tris[ti++] = below;
                }
            }

            // South cap
            int southPole = vertCount - 1;
            int lastRowStart = 1 + (rings - 2) * slices;
            for (int s = 0; s < slices; s++)
            {
                tris[ti++] = southPole;
                tris[ti++] = lastRowStart + s;
                tris[ti++] = lastRowStart + (s + 1) % slices;
            }

            var mesh = new Mesh { name = "BrushSphere" };
            mesh.vertices  = verts;
            mesh.normals   = normals;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }

        // ----------------------------------------------------------------
        //  Cube (24 verts for proper face normals)
        // ----------------------------------------------------------------

        private static Mesh GenerateCube()
        {
            float s = 0.5f;
            var verts = new Vector3[24];
            var normals = new Vector3[24];

            // Face definitions: normal, then 4 corner offsets
            Vector3[] faceNormals =
            {
                Vector3.up,    Vector3.down,
                Vector3.right, Vector3.left,
                Vector3.forward, Vector3.back
            };

            Vector3[][] faceCorners =
            {
                // +Y
                new[] { new Vector3(-s, s, -s), new Vector3(-s, s, s), new Vector3(s, s, s), new Vector3(s, s, -s) },
                // -Y
                new[] { new Vector3(-s, -s, s), new Vector3(-s, -s, -s), new Vector3(s, -s, -s), new Vector3(s, -s, s) },
                // +X
                new[] { new Vector3(s, -s, -s), new Vector3(s, -s, s), new Vector3(s, s, s), new Vector3(s, s, -s) },
                // -X
                new[] { new Vector3(-s, -s, s), new Vector3(-s, -s, -s), new Vector3(-s, s, -s), new Vector3(-s, s, s) },
                // +Z
                new[] { new Vector3(-s, -s, s), new Vector3(s, -s, s), new Vector3(s, s, s), new Vector3(-s, s, s) },
                // -Z
                new[] { new Vector3(s, -s, -s), new Vector3(-s, -s, -s), new Vector3(-s, s, -s), new Vector3(s, s, -s) }
            };

            var tris = new int[36];
            for (int face = 0; face < 6; face++)
            {
                int vi = face * 4;
                for (int c = 0; c < 4; c++)
                {
                    verts[vi + c]   = faceCorners[face][c];
                    normals[vi + c] = faceNormals[face];
                }

                int ti = face * 6;
                tris[ti + 0] = vi;
                tris[ti + 1] = vi + 1;
                tris[ti + 2] = vi + 2;
                tris[ti + 3] = vi;
                tris[ti + 4] = vi + 2;
                tris[ti + 5] = vi + 3;
            }

            var mesh = new Mesh { name = "BrushCube" };
            mesh.vertices  = verts;
            mesh.normals   = normals;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }

        // ----------------------------------------------------------------
        //  Octahedron / Diamond (6 verts, 8 tris)
        // ----------------------------------------------------------------

        /// <param name="yScale">1.0 for regular octahedron, 1.5 for diamond shape.</param>
        private static Mesh GenerateOctahedron(float yScale)
        {
            float s = 0.5f;
            float ys = 0.5f * yScale;

            // 6 vertices: top, bottom, front, back, left, right
            // For proper face normals we duplicate vertices per-face (24 verts, 8 faces)
            var verts   = new Vector3[24];
            var normals = new Vector3[24];
            var tris    = new int[24]; // 8 triangles * 3

            Vector3 top   = new Vector3(0,  ys, 0);
            Vector3 bot   = new Vector3(0, -ys, 0);
            Vector3 front = new Vector3(0, 0,  s);
            Vector3 back  = new Vector3(0, 0, -s);
            Vector3 left  = new Vector3(-s, 0, 0);
            Vector3 right = new Vector3( s, 0, 0);

            // 8 faces: top-front-right, top-right-back, top-back-left, top-left-front
            //          bot-right-front, bot-back-right, bot-left-back, bot-front-left
            Vector3[][] faces =
            {
                new[] { top, front, right },
                new[] { top, right, back },
                new[] { top, back,  left },
                new[] { top, left,  front },
                new[] { bot, right, front },
                new[] { bot, back,  right },
                new[] { bot, left,  back },
                new[] { bot, front, left }
            };

            for (int f = 0; f < 8; f++)
            {
                int vi = f * 3;
                verts[vi]     = faces[f][0];
                verts[vi + 1] = faces[f][1];
                verts[vi + 2] = faces[f][2];

                Vector3 n = Vector3.Cross(
                    faces[f][1] - faces[f][0],
                    faces[f][2] - faces[f][0]).normalized;
                normals[vi]     = n;
                normals[vi + 1] = n;
                normals[vi + 2] = n;

                tris[vi]     = vi;
                tris[vi + 1] = vi + 1;
                tris[vi + 2] = vi + 2;
            }

            string meshName = yScale > 1.01f ? "BrushDiamond" : "BrushOctahedron";
            var mesh = new Mesh { name = meshName };
            mesh.vertices  = verts;
            mesh.normals   = normals;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
