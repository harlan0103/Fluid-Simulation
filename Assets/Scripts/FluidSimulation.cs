using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Mathematics;

public class FluidSimulation : MonoBehaviour
{
    public LineRenderer boundingBoxRenderer;
    public Vector2 boundSize;

    [Header("GPU Instancing Draw Particles")]
    public int numParticles = 1;
    public Mesh mesh;
    public float particleSize = 0.08f;
    public Shader shader;
    private Material particleMat;
    private ComputeBuffer argsBuffer;               // Dispatch argument
    private uint[] args = new uint[5] { 0, 0, 0, 0, 0 };
    private int instanceCount;
    private const int subMeshIndex = 0;

    [Header("Particle Properties")]
    public float gravity = 5;
    [Range(0.1f, 1.0f)]
    public float collisionDamping = 0.8f;

    [Header("Fluid Simulation Attributes")]
    public float smoothRadius = 0.35f;
    public float targetDensity = 55f;
    public float pressureMultiplier = 500f;

    // Num of segments to create a circle mesh
    private static int segments = 100;

    // Private groups
    private List<GameObject> particleList;
    private System.Random random = new System.Random();

    // Spatial grid structure
    private Entry[] spatialLookUp;
    private int[] startIndices;
    private Vector2[] cellOffsets;

    // Compute shader and buffers
    private ComputeShader computeShader;
    private ComputeBuffer positionBuffer;
    private ComputeBuffer predictedPositionBuffer;
    private ComputeBuffer velocityBuffer;
    private ComputeBuffer densityBuffer;

    // Data arrays
    private Vector2[] predictedPosition;
    private Vector2[] positions;
    private Vector2[] velocities;
    public float[] densities;

    // Compute data arrays
    private float2[] computePredictedPosition;
    private float2[] computePositions;
    private float2[] computeVelocities;
    public float[] computeDensities;

    // Start is called before the first frame update
    void Start()
    {
        // Draw boundary
        DrawBoundary();

        // Initialize compute shader
        InitializeComputeBuffers();
    }

    // Update is called once per frame
    void Update()
    {
        UpdateSettings(Time.deltaTime);
        RunSimulationStep();
    }

    private void LateUpdate()
    {
        particleMat.SetFloat("_Scale", particleSize);
        Graphics.DrawMeshInstancedIndirect(mesh, subMeshIndex, particleMat, new Bounds(Vector3.zero, new Vector3(100.0f, 100.0f, 100.0f)), argsBuffer);
    }

    void InitializeComputeBuffers()
    {
        computeShader = Resources.Load<ComputeShader>("FluidSim");

        // Initialize Buffers
        positionBuffer = new ComputeBuffer(numParticles, sizeof(float) * 2);
        predictedPositionBuffer = new ComputeBuffer(numParticles, sizeof(float) * 2);
        velocityBuffer = new ComputeBuffer(numParticles, sizeof(float) * 2);
        densityBuffer = new ComputeBuffer(numParticles, sizeof(float) * 2);

        instanceCount = numParticles;
        argsBuffer = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);

        // Initialize data arrays
        computePredictedPosition = new float2[numParticles];
        computePositions = new float2[numParticles];
        computeVelocities = new float2[numParticles];
        computeDensities = new float[numParticles];

        // Initialize particles
        computePositions = GenerateParticles(numParticles);
        computePredictedPosition = computePositions;

        // Set data arrays to buffers
        positionBuffer.SetData(computePositions);
        predictedPositionBuffer.SetData(computePredictedPosition);
        velocityBuffer.SetData(computeVelocities);
        densityBuffer.SetData(computeDensities);

        // Initialize compute buffers
        computeShader.SetBuffer(0, "Positions", positionBuffer);
        computeShader.SetBuffer(3, "Positions", positionBuffer);

        computeShader.SetBuffer(0, "PredictedPositions", predictedPositionBuffer);
        computeShader.SetBuffer(1, "PredictedPositions", predictedPositionBuffer);
        computeShader.SetBuffer(2, "PredictedPositions", predictedPositionBuffer);

        computeShader.SetBuffer(1, "Densities", densityBuffer);
        computeShader.SetBuffer(2, "Densities", densityBuffer);

        computeShader.SetBuffer(0, "Velocities", velocityBuffer);
        computeShader.SetBuffer(2, "Velocities", velocityBuffer);
        computeShader.SetBuffer(3, "Velocities", velocityBuffer);

        // Set attribute values
        UpdateSettings(Time.deltaTime);

        // Create a new material for shader
        particleMat = new Material(shader);

        // Set attributes
        particleMat.SetBuffer("_Positions", positionBuffer);
        particleMat.SetFloat("_Scale", particleSize);
        particleMat.SetColor("_Color", Colour.lightblue);

        UpdateBuffers();
    }

    // https://docs.unity3d.com/ScriptReference/Graphics.DrawMeshInstancedIndirect.html
    void UpdateBuffers()
    {
        // Indirect args
        if (mesh != null)
        {
            args[0] = (uint)mesh.GetIndexCount(subMeshIndex);
            args[1] = (uint)instanceCount;
            args[2] = (uint)mesh.GetIndexStart(subMeshIndex);
            args[3] = (uint)mesh.GetBaseVertex(subMeshIndex);
        }
        else
        {
            args[0] = args[1] = args[2] = args[3] = 0;
        }
        argsBuffer.SetData(args);
    }

    void RunSimulationStep()
    {
        Dispatch(computeShader, numParticles, kernelIndex: 0);
        Dispatch(computeShader, numParticles, kernelIndex: 1);
        Dispatch(computeShader, numParticles, kernelIndex: 2);
        Dispatch(computeShader, numParticles, kernelIndex: 3);
    }

    void UpdateSettings(float deltaTime)
    {
        computeShader.SetInt("numParticles", numParticles);
        computeShader.SetFloat("deltaTime", deltaTime);
        computeShader.SetFloat("gravity", gravity);
        computeShader.SetFloat("collisionDamping", collisionDamping);
        computeShader.SetFloat("smoothingRadius", smoothRadius);
        computeShader.SetFloat("targetDensity", targetDensity);
        computeShader.SetFloat("pressureMultiplier", pressureMultiplier);
        computeShader.SetVector("boundsSize", boundSize);

        computeShader.SetFloat("SpikyPow2ScalingFactor", 6 / (Mathf.PI * Mathf.Pow(smoothRadius, 4)));
        computeShader.SetFloat("SpikyPow2DerivativeScalingFactor", 12 / (Mathf.Pow(smoothRadius, 4) * Mathf.PI));
    }

    Vector2 CalculateViewPortSize()
    {
        Camera cam = Camera.main;
        float half_fov = cam.fieldOfView / 2.0f;
        float zDis = Mathf.Abs(cam.transform.position.z);

        // Calculate width and height of the view port size
        int ViewPortHeight = (int)(zDis * Mathf.Tan(Mathf.Deg2Rad * half_fov) * 2);
        int ViewPortWidth = ViewPortHeight * Screen.width / Screen.height;

        return new Vector2(ViewPortWidth, ViewPortHeight);
    }

    void DrawBoundary()
    {
        if (boundSize == Vector2.zero || boundSize == null)
        {
            return;
        }

        if (boundingBoxRenderer == null)
        {
            return;
        }

        boundingBoxRenderer.positionCount = 5;

        float bottomLeftCornerX = 0f - boundSize.x / 2;
        float bottomLeftCornerY = 0f - boundSize.y / 2;

        float[] xOffSet = { 0, boundSize.x, boundSize.x, 0, 0 };
        float[] yOffSet = { 0, 0, boundSize.y, boundSize.y, 0 };
        for (int i = 0; i < 5; i++)
        {
            Vector2 cornerPoint = new Vector2(bottomLeftCornerX + xOffSet[i], bottomLeftCornerY + yOffSet[i]);
            boundingBoxRenderer.SetPosition(i, cornerPoint);
        }
    }

    float2[] GenerateParticles(int particleCount)
    {
        float2 s = new float2(7, 7);

        int numX = Mathf.CeilToInt(Mathf.Sqrt(s.x / s.y * particleCount));
        int numY = Mathf.CeilToInt(particleCount / (float)numX);
        int i = 0;

        float2[] particles = new float2[numParticles];

        for (int y = 0; y < numY; y++)
        {
            for (int x = 0; x < numX; x++)
            {
                if (i >= particleCount) break;

                float tx = numX <= 1 ? 0.5f : x / (numX - 1f);
                float ty = numY <= 1 ? 0.5f : y / (numY - 1f);

                particles[i] = new float2((tx - 0.5f) * 7f, (ty - 0.5f) * 7f) + new float2(0, 0);

                i++;
            }
        }

        return particles;
    }

    // Calculate thread group number
    public static void Dispatch(ComputeShader computeShader, int numParticles, int kernelIndex)
    {
        uint x, y, z;
        computeShader.GetKernelThreadGroupSizes(kernelIndex, out x, out y, out z);  // (8, 1, 1)

        int numGroupsX = Mathf.CeilToInt(numParticles / (float)x);
        int numGroupsY = Mathf.CeilToInt(1 / (float)y);
        int numGroupsZ = Mathf.CeilToInt(1 / (float)z);

        computeShader.Dispatch(kernelIndex, numGroupsX, numGroupsY, numGroupsZ);
    }

    void RandomCreateParticles(int seed)
    {
        System.Random rng = new(seed);
        positions = new Vector2[numParticles];
        predictedPosition = new Vector2[numParticles];
        velocities = new Vector2[numParticles];

        for (int i = 0; i < positions.Length; i++)
        {
            float x = (float)(rng.NextDouble() - 0.5) * boundSize.x;
            float y = (float)(rng.NextDouble() - 0.5) * boundSize.y;

            positions[i] = new Vector2(x, y);
            predictedPosition[i] = new Vector2(x, y);
        }
    }

    void UniformCreateParticles()
    {
        // Create particle arrays
        positions = new Vector2[numParticles];
        predictedPosition = new Vector2[numParticles];
        velocities = new Vector2[numParticles];

        // Place particles in a grid formation
        int particlesPerRow = (int)Mathf.Sqrt(numParticles);
        int particlesPerCol = (numParticles - 1) / particlesPerRow + 1;
        float spacing = particleSize * 2;

        for (int i = 0; i < numParticles; i++)
        {
            float x = (i % particlesPerRow - particlesPerRow / 2f + 0.5f) * spacing;
            float y = (i / particlesPerRow - particlesPerCol / 2f + 0.5f) * spacing;
            positions[i] = new Vector2(x, y);
            predictedPosition[i] = new Vector2(x, y);
        }
    }

    void UpdateParticleMovement(float deltaTime)
    {
        // Apply gravity and predict next position
        Parallel.For(0, numParticles, i =>
        {
            velocities[i] += Vector2.down * gravity * deltaTime;
            predictedPosition[i] = positions[i] + velocities[i] * 1 / 120;
        });

        // Update spatial lookup with predicted position
        UpdateSpatialLookup(predictedPosition, smoothRadius);

        // Calculate densities
        Parallel.For(0, numParticles, i =>
        {
            densities[i] = CalculateDensity(predictedPosition[i]);
        });

        // Calculate and apply pressure force
        Parallel.For(0, numParticles, i =>
        {
            Vector2 pressureForce = CalculatePressureForce(i);
            Vector2 pressureAcceleration = pressureForce / densities[i];
            velocities[i] += pressureAcceleration * deltaTime;
        });

        // Update positions and resolve collisions
        Parallel.For(0, numParticles, i =>
        {
            positions[i] += velocities[i] * deltaTime;
            ResolveCollisions(ref positions[i], ref velocities[i]);
        });
    }

    void DrawParticles()
    {
        // Delete all existing particles first
        if (particleList.Count != 0)
        {
            foreach (GameObject particle in particleList)
            {
                Destroy(particle);
            }
        }
        
        for (int i = 0; i < numParticles; i++)
        {
            DrawCircle(positions[i], particleSize, Colour.lightblue);
        }

        foreach (Transform trans in transform)
        {
            particleList.Add(trans.gameObject);
        }
    }

    void DrawCircle(Vector2 position, float radius, Color colour)
    {
        Vector3[] vertices = new Vector3[segments + 1];
        Vector2[] uvs = new Vector2[vertices.Length];
        int[] triangles = new int[segments * 3];

        vertices[0] = new Vector3(position.x, position.y, 0);  // center vertex
        uvs[0] = new Vector2(0.5f, 0.5f);

        for (int i = 1, t = 0; i < vertices.Length; i++, t += 3)
        {
            float angle = (float)(i - 1) / segments * 360 * Mathf.Deg2Rad;
            float x = Mathf.Sin(angle) * radius + position.x;
            float y = Mathf.Cos(angle) * radius + position.y;

            vertices[i] = new Vector3(x, y, 0);
            uvs[i] = new Vector2((x / radius + 1) * 0.5f, (y / radius + 1) * 0.5f);

            if (i < vertices.Length - 1)
            {
                triangles[t] = 0;
                triangles[t + 1] = i;
                triangles[t + 2] = i + 1;
            }
            else // for the last segment
            {
                triangles[t] = 0;
                triangles[t + 1] = i;
                triangles[t + 2] = 1; // close the circle by connecting to the first outer vertex
            }
        }

        Mesh mesh = new Mesh();

        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;

        // Assign material instance to each particle
        Material newMat = new Material(particleMat);
        newMat.SetColor("_Color", colour);

        // Create a new particle object
        GameObject newObj = new GameObject("particle");
        newObj.AddComponent<MeshFilter>();
        newObj.AddComponent<MeshRenderer>();
        newObj.GetComponent<MeshFilter>().mesh = mesh;
        newObj.GetComponent<MeshRenderer>().material = newMat;

        // Add script and tag for debugging
        newObj.AddComponent<ParticleAtt>();
        newObj.layer = LayerMask.NameToLayer("Particles");
        newObj.AddComponent<MeshCollider>();

        // Add it into the list
        newObj.transform.SetParent(transform);
    }

    void ResolveCollisions(ref Vector2 position, ref Vector2 velocity)
    {
        Vector2 halfBoundSize = boundSize / 2 - Vector2.one * particleSize;

        if (Mathf.Abs(position.x) > halfBoundSize.x)
        {
            position.x = halfBoundSize.x * Mathf.Sign(position.x);
            velocity.x *= -1 * collisionDamping;
        }

        if (Mathf.Abs(position.y) > halfBoundSize.y)
        {
            position.y = halfBoundSize.y * Mathf.Sign(position.y);
            velocity.y *= -1 * collisionDamping;
        }
    }

    public List<GameObject> GetParticleList()
    {
        if (particleList != null)
        {
            return particleList;
        }

        return null;
    }

    // ==========================================
    //
    // Property Calculation for each particles
    // 
    // ==========================================
    static float SmoothKernal(float dst, float radius)
    {
        if (dst >= radius)
        {
            return 0;
        }

        float volume = (Mathf.PI * Mathf.Pow(radius, 4)) / 6;
        return (radius - dst) * (radius - dst) / volume;
    }

    static float SmoothKernalDerivative(float dst, float radius)
    {
        if (dst >= radius) return 0;

        float scale = 12 / (Mathf.Pow(radius, 4) * Mathf.PI);
        return (dst - radius) * scale;
    }

    float CalculateSharedPressure(float density, float density2)
    {
        float pressureA = ConvertDensityToPressure(density);
        float pressureB = ConvertDensityToPressure(density2);

        return (pressureA + pressureB) / 2;
    }

    float ConvertDensityToPressure(float density)
    {
        float densityError = density - targetDensity;
        float pressure = densityError * pressureMultiplier;
        return pressure;
    }

    // Function to generate a random 2D vector direction
    public Vector2 Random2DVectorDirection()
    {
        // Generate a random angle in radians
        float angleRadians = (float)random.NextDouble() * 2 * Mathf.PI;

        // Calculate the x and y components of the 2D vector
        float x = Mathf.Cos(angleRadians);
        float y = Mathf.Sin(angleRadians);

        // Return the 2D vector as a Vector2
        return new Vector2(x, y);
    }

    float CalculateDensity(Vector2 samplePoint)
    {
        float density = 0;
        const float mass = 1;

        // Loop over all particle positions
        // Optimized with data structure instead
        List<int> pointsIdx = ForeachPointWithinRadius(samplePoint);
        foreach (int idx in pointsIdx)
        {
            float dist = (predictedPosition[idx] - samplePoint).magnitude;
            float influence = SmoothKernal(dist, smoothRadius);

            density += mass * influence;
        }

        return density;
    }

    void UpdateDensities()
    {
        if (densities == null)
        {
            densities = new float[numParticles];
        }

        Parallel.For(0, numParticles, i =>
        {
            densities[i] = CalculateDensity(positions[i]);
        });
    }

    Vector2 CalculatePressureForce(int particleIdx)
    {
        Vector2 pressureForce = Vector2.zero;

        //List<int> particleIdxList = ForeachPointWithinRadius(predictPositions[particleIdx]);
        for (int otherParticleIndex = 0; otherParticleIndex < numParticles; otherParticleIndex++)
        {
            //int otherParticleIndex = particleIdxList[i];

            if (particleIdx == otherParticleIndex) continue;

            Vector2 offset = predictedPosition[otherParticleIndex] - predictedPosition[particleIdx];
            float dst = offset.magnitude;
            Vector2 dir = dst == 0 ? offset / 0.01f : offset / dst;
            float slope = SmoothKernalDerivative(dst, smoothRadius);
            float density = densities[otherParticleIndex];

            float sharedPressure = CalculateSharedPressure(density, densities[particleIdx]);
            pressureForce += sharedPressure * dir * slope / density;
        }

        return pressureForce;
    }

    // ==========================================
    //
    // Spatial grid structure
    // 
    // Spatial key lookup logics to speedup the process finding particles have impact to the sample point
    // Consider there are 10 particles in the scene
    // [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
    // For point 0
    // Point index is: 0
    // Cell coord is: (2, 0) based on point (x,y) and smoothRadius
    // Cell hash is 6613 with the cell key is 3
    // We do the same approach to all points
    // So that in the spatialLookup array, it becomes
    // [3, 6, 2, 9, 9, 2, 6, 2, 9, 9]
    // Based on that we call tell that having the same hash key values meaning those points are in the same cell grid
    // Then we can sort the SpatialLookup array to have points having same hash key group together
    // points index:    [2, 5, 7, 0, 1, 6, 3, 4, 8, 9]
    // points hashkey:  [2, 2, 2, 3, 6, 6, 9, 9, 9, 9]
    // Then based on the hashkey array we have, we can then generate a start index array for each hash key
    // Start index:     [-, -, 0, 3, -, -, 4, -, -, 6]
    // Start index array provide a way to look up all points in the same grid
    // For example, we calculate the hashkey for current grid is 9
    // Then startIndicies[9] = 6, meaning that all points with hashKey 9 start from lookup array index 6
    // So we have points [3, 4, 8, 9] are all in the same grid with hashkey 9
    // 
    // Then for each sample point, we can first locate which grid it is inside
    // Then we calculate the total 9 cells around and including the center cell
    // For each cell calculate the hashkey
    // Then use startIndices array to find all points that inside of the cell
    // For each point that reside inside of the 3x3 grid
    // We then check if it is also inside of the smoothcircle of the sample point
    // If inside, we then will update the properties of each point
    // ==========================================

    void InitializeSpatialStructure()
    {
        spatialLookUp = new Entry[numParticles];
        startIndices = new int[numParticles];

        // Use offset to find all 3x3 cell around the center cell
        cellOffsets = new Vector2[]
        {
            new Vector2(0, 0),                  // Current cell
            new Vector2(1, 0),                  // Right one
            new Vector2(-1, 0),                 // Left one
            new Vector2(0, 1),                  // Top one
            new Vector2(0, -1),                 // Bottom one
            new Vector2(1, 1),                  // Upper right one
            new Vector2(1, -1),                 // Bottom right one
            new Vector2(-1, 1),                 // Upper left one
            new Vector2(-1, -1)                 // Bottom left one
        };
    }

    public void UpdateSpatialLookup(Vector2[] points, float radius)
    {
        // Create (unordered) saptial lookup
        Parallel.For(0, points.Length, i =>
        {
            (int cellX, int cellY) = PositionToCellCoord(points[i], radius);
            uint cellKey = GetKeyFromHash(HashCell(cellX, cellY));
            spatialLookUp[i] = new Entry(i, cellKey);
            startIndices[i] = int.MaxValue;     // Reset start index
        });

        // Sort by cell key
        Array.Sort(spatialLookUp);

        // Set the first key start position
        uint firstKey = spatialLookUp[0].hashKey;
        startIndices[firstKey] = 0;

        // Calculate start indices of each unique cell key in the spatial lookup
        Parallel.For(0, points.Length, i =>
        {
            uint key = spatialLookUp[i].hashKey;
            uint keyPrev = i == 0 ? uint.MinValue : spatialLookUp[i - 1].hashKey;
            if (key != keyPrev)
            {
                startIndices[key] = i;
            }
        });
    }

    // Convert a position to the coordinate of the cell it is within
    // Radius is the smoothRadius, which is the size of the grid
    // The return value is the index of given point in the specific grid
    public (int x, int y) PositionToCellCoord(Vector2 point, float radius)
    {
        int cellX = (int)((point.x - (0 - boundSize.x / 2)) / radius);
        int cellY = (int)((point.y - (0 - boundSize.y / 2)) / radius);

        return (cellX, cellY);
    }

    // Convert a cell coordinate into a single number
    // Hash collisions are unavoidable, but we want to at least
    // try to minimize collisions for nearby cells.
    public uint HashCell(int cellX, int cellY)
    {
        uint a = (uint)cellX * 15823;
        uint b = (uint)cellY * 9737333;

        return a + b;
    }

    // Wrap the hash value around the length of the array
    public uint GetKeyFromHash(uint hash)
    {
        return hash % (uint)spatialLookUp.Length;
    }

    public List<int> ForeachPointWithinRadius(Vector2 samplePoint)
    {
        List<int> validPointsInsideRadius = new List<int>();

        // Find which cell the sample point is in (this will be the centre of the 3x3 grid)
        (int centreX, int centreY) = PositionToCellCoord(samplePoint, smoothRadius);

        // Loop over all cells of the 3x3 block around the centre cell
        foreach (Vector2 offset in cellOffsets)
        {
            int offsetX = (int)offset.x;
            int offsetY = (int)offset.y;

            // Get the key of current cell, then loop over all points that share that key
            uint key = GetKeyFromHash(HashCell(centreX + offsetX, centreY + offsetY));
            int cellStartIndex = startIndices[key];

            for (int i = cellStartIndex; i < spatialLookUp.Length; i++)
            {
                // Exit loop if we're no longer looking at the correct cell
                if (spatialLookUp[i].hashKey != key) break;

                int particleIndex = spatialLookUp[i].index;

                float sqrDst = (positions[particleIndex] - samplePoint).sqrMagnitude;
                float sqrRadius = smoothRadius * smoothRadius;

                // Test if the point is inside the radius
                if (sqrDst <= sqrRadius)
                {
                    // Add valid particle index into the list
                    validPointsInsideRadius.Add(particleIndex);
                }
            }
        }

        return validPointsInsideRadius;
    }

    // For debug using
    // Update particle information including position, hashkey and cell coordinate
    public void UpdateParticleInfomation()
    { 
        for (int i = 0; i < particleList.Count; i++)
        {
            // Update particle information
            GameObject particle = particleList[i];
            Entry particleEntry = spatialLookUp[i];
            (int cellX, int cellY) = PositionToCellCoord(positions[i], smoothRadius);
            particle.GetComponent<ParticleAtt>().UpdateParticleInfo(i, positions[i], particleEntry.hashKey, cellX, cellY);
        }
    }

    void OnDestroy()
    {
        if (positionBuffer != null)
        {
            positionBuffer.Release();
        }
        positionBuffer = null;

        if (predictedPositionBuffer != null)
        {
            predictedPositionBuffer.Release();
        }
        predictedPositionBuffer = null;

        if (velocityBuffer != null)
        {
            velocityBuffer.Release();
        }
        velocityBuffer = null;

        if (densityBuffer != null)
        {
            densityBuffer.Release();
        }
        densityBuffer = null;
    }
}
