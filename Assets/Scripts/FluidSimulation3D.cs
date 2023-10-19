using System;
using Unity.Mathematics;
using UnityEngine;

public class FluidSimulation3D : MonoBehaviour
{
    [Header("Scene Setting")]
    public float3 boundingBox = new float3(10f, 11f, 6f);
    public float iterationsPerframe = 3;
    public float timeScale = 0.8f;

    [Header("Particle Instance")]
    public Mesh mesh;
    public float particleSize = 0.08f;
    public Shader shader;
    public float3 spawnCenter;
    public int3 spawnCubeSize;
    public float offset;
    public Color particleColor;
    private Material particleMat;
    private ComputeBuffer argsBuffer;               // Dispatch argument
    private uint[] args = new uint[5] { 0, 0, 0, 0, 0 };
    private int instanceCount;
    private const int subMeshIndex = 0;
    private int numParticles;

    [Header("Particle Properties")]
    public float gravity = 5;
    [Range(0.1f, 1.0f)]
    public float collisionDamping = 0.8f;

    [Header("Fluid Simulation Attributes")]
    public float smoothRadius = 0.35f;
    public float targetDensity = 55f;
    public float pressureMultiplier = 500f;

    [Header("Debug")]
    public bool colorDebugOn = false;
    public int targetIndex = 0;
    public Color debugColor;

    // Compute shader and buffers
    private ComputeShader computeShader;
    private ComputeBuffer positionBuffer;
    private ComputeBuffer predictedPositionBuffer;
    private ComputeBuffer velocityBuffer;
    private ComputeBuffer densityBuffer;

    private ComputeBuffer particleIndexBuffer;          // sorted particle indices array
    private ComputeBuffer particleCellIndexBuffer;      // sorted hash cell index for given particle
    private ComputeBuffer cellOffsetBuffer;             // offset of cell number

    private ComputeBuffer colorBuffer;


    // Compute data arrays
    private float3[] computePositions;
    private float3[] predictedPositions;
    private float3[] velocities;
    private float[] densities;
    private Vector3[] colors;

    // Can be public for debug purpose
    private uint[] particleIndicies;
    private uint[] particleCellIndicies;
    private uint[] cellOffsets;

    private uint[] keyValueDebugger;     // For debug use

    public enum kernels
    { 
        CalculateVelocity = 0,
        CalculateDensities = 1,
        CalculatePressureForce = 2,
        UpdatePositions = 3,
        HashParticles = 4,
        BitonicSort = 5,
        CalculateCellOffsets = 6,
        ClearCellOffsets = 7
    }

    void Start()
    { 
        InitializeComputeBuffers();
    }

    void Update()
    {
        if (Time.frameCount > 10)
        {
            float deltaTime = Time.deltaTime / iterationsPerframe * timeScale;

            for (int i = 0; i < iterationsPerframe; i++)
            {
                UpdateSettings(deltaTime);
                RunSimulationStep();
            }
        }
    }

    void LateUpdate()
    {
        particleMat.SetFloat("_Scale", particleSize);
        Graphics.DrawMeshInstancedIndirect(mesh, subMeshIndex, particleMat, new Bounds(Vector3.zero, new Vector3(100.0f, 100.0f, 100.0f)), argsBuffer);
    }

    void InitializeComputeBuffers()
    {
        computeShader = Resources.Load<ComputeShader>("FluidSim3D");

        numParticles = spawnCubeSize.x * spawnCubeSize.y * spawnCubeSize.z;

        boundingBox = gameObject.GetComponent<BoundingBoxController>().GetBoundingBox();

        // Initialize Buffers
        positionBuffer = new ComputeBuffer(numParticles, sizeof(float) * 3);
        predictedPositionBuffer = new ComputeBuffer(numParticles, sizeof(float) * 3);
        velocityBuffer = new ComputeBuffer(numParticles, sizeof(float) * 3);
        densityBuffer = new ComputeBuffer(numParticles, sizeof(float));
        particleIndexBuffer = new ComputeBuffer(numParticles, sizeof(int));
        particleCellIndexBuffer = new ComputeBuffer(numParticles, sizeof(int));
        cellOffsetBuffer = new ComputeBuffer(numParticles, sizeof(int));
        colorBuffer = new ComputeBuffer(numParticles, sizeof(float) * 3);

        instanceCount = numParticles;
        argsBuffer = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);

        // Initialize data arrays
        computePositions = new float3[numParticles];
        predictedPositions = new float3[numParticles];
        velocities = new float3[numParticles];
        densities = new float[numParticles];
        particleIndicies = new uint[numParticles];
        particleCellIndicies = new uint[numParticles];
        cellOffsets = new uint[numParticles];
        colors = new Vector3[numParticles];
        keyValueDebugger = new uint[numParticles];

        // Initialize particles
        computePositions = GenerateParticles(numParticles);
        predictedPositions = computePositions;
        
        for (uint i = 0; i < numParticles; i++)
        {
            particleIndicies[i] = i;
        }
        
        // Color debugger intialize
        for (uint i = 0; i < numParticles; i++)
        {
            colors[i] = new float3(particleColor.r, particleColor.g, particleColor.b);
        }
        if (colorDebugOn)
        {
            colors[targetIndex] = new float3(Color.red.r, Color.red.g, Color.red.b);
        }

        // Set data arrays to buffers
        positionBuffer.SetData(computePositions);
        predictedPositionBuffer.SetData(predictedPositions);
        velocityBuffer.SetData(velocities);
        densityBuffer.SetData(densities);
        particleIndexBuffer.SetData(particleIndicies);
        particleCellIndexBuffer.SetData(particleCellIndicies);
        cellOffsetBuffer.SetData(cellOffsets);
        colorBuffer.SetData(colors);

        // Initialize compute buffers
        computeShader.SetBuffer(0, "Positions", positionBuffer);
        computeShader.SetBuffer(3, "Positions", positionBuffer);
        computeShader.SetBuffer(4, "Positions", positionBuffer);

        computeShader.SetBuffer(0, "PredictedPositions", predictedPositionBuffer);
        computeShader.SetBuffer(1, "PredictedPositions", predictedPositionBuffer);
        computeShader.SetBuffer(2, "PredictedPositions", predictedPositionBuffer);

        computeShader.SetBuffer(0, "Velocities", velocityBuffer);
        computeShader.SetBuffer(2, "Velocities", velocityBuffer);
        computeShader.SetBuffer(3, "Velocities", velocityBuffer);

        computeShader.SetBuffer(1, "Densities", densityBuffer);
        computeShader.SetBuffer(2, "Densities", densityBuffer);

        computeShader.SetBuffer((int)kernels.CalculateDensities, "ParticleIndicies", particleIndexBuffer);
        computeShader.SetBuffer((int)kernels.CalculatePressureForce, "ParticleIndicies", particleIndexBuffer);
        computeShader.SetBuffer((int)kernels.HashParticles, "ParticleIndicies", particleIndexBuffer);
        computeShader.SetBuffer((int)kernels.BitonicSort, "ParticleIndicies", particleIndexBuffer);
        computeShader.SetBuffer((int)kernels.CalculateCellOffsets, "ParticleIndicies", particleIndexBuffer);

        computeShader.SetBuffer((int)kernels.CalculateDensities, "ParticleCellIndicies", particleCellIndexBuffer);
        computeShader.SetBuffer((int)kernels.CalculatePressureForce, "ParticleCellIndicies", particleCellIndexBuffer);
        computeShader.SetBuffer((int)kernels.HashParticles, "ParticleCellIndicies", particleCellIndexBuffer);
        computeShader.SetBuffer((int)kernels.BitonicSort, "ParticleCellIndicies", particleCellIndexBuffer);
        computeShader.SetBuffer((int)kernels.CalculateCellOffsets, "ParticleCellIndicies", particleCellIndexBuffer);

        computeShader.SetBuffer((int)kernels.CalculateDensities, "CellOffset", cellOffsetBuffer);
        computeShader.SetBuffer((int)kernels.CalculatePressureForce, "CellOffset", cellOffsetBuffer);
        computeShader.SetBuffer((int)kernels.HashParticles, "CellOffset", cellOffsetBuffer);
        computeShader.SetBuffer((int)kernels.CalculateCellOffsets, "CellOffset", cellOffsetBuffer);
        computeShader.SetBuffer((int)kernels.ClearCellOffsets, "CellOffset", cellOffsetBuffer);

        computeShader.SetBuffer((int)kernels.CalculateDensities, "ColorBuffer", colorBuffer);
        computeShader.SetBuffer((int)kernels.ClearCellOffsets, "ColorBuffer", colorBuffer);

        // Set attribute values
        UpdateSettings(Time.deltaTime);

        // Create a new material for shader
        particleMat = new Material(shader);

        // Set attributes
        particleMat.SetBuffer("_Positions", positionBuffer);
        particleMat.SetBuffer("_DebugColors", colorBuffer);
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

    void UpdateSettings(float deltaTime)
    {
        // Attributes
        computeShader.SetInt("numParticles", numParticles);
        computeShader.SetFloat("deltaTime", deltaTime);
        computeShader.SetFloat("gravity", gravity);
        computeShader.SetFloat("collisionDamping", collisionDamping);
        computeShader.SetVector("boundsSize", transform.localScale);
        computeShader.SetFloat("smoothingRadius", smoothRadius);
        computeShader.SetFloat("targetDensity", targetDensity);
        computeShader.SetFloat("pressureMultiplier", pressureMultiplier);

        // Color debugger
        computeShader.SetVector("debugColor", new Vector3(debugColor.r, debugColor.g, debugColor.b));
        computeShader.SetVector("defaultColor", new Vector3(particleColor.r, particleColor.g, particleColor.b));
        computeShader.SetVector("testParticleColor", new Vector3(Color.red.r, Color.red.g, Color.red.b));
        computeShader.SetInt("targetIndex", targetIndex);
        computeShader.SetBool("debugOn", colorDebugOn);

        // Factors
        computeShader.SetFloat("radius2", smoothRadius * smoothRadius);
        computeShader.SetFloat("radius3", smoothRadius * smoothRadius * smoothRadius);
        computeShader.SetFloat("SpikyPow2ScalingFactor", 15 / (2 * Mathf.PI * Mathf.Pow(smoothRadius, 5)));
        computeShader.SetFloat("DerivativeSpikyPow2ScalingFactor", 15 / (Mathf.Pow(smoothRadius, 5) * Mathf.PI));

        // Matrix
        computeShader.SetMatrix("localToWorldMatrix", transform.localToWorldMatrix);
        computeShader.SetMatrix("WorldToLocalMatrix", transform.worldToLocalMatrix);
    }

    void RunSimulationStep()
    {
        Dispatch(computeShader, numParticles, (int)kernels.ClearCellOffsets);
        Dispatch(computeShader, numParticles, (int)kernels.HashParticles);
        SortPartices();
        Dispatch(computeShader, numParticles, (int)kernels.CalculateCellOffsets);

        Dispatch(computeShader, numParticles, 0);
        Dispatch(computeShader, numParticles, 1);
        Dispatch(computeShader, numParticles, 2);
        Dispatch(computeShader, numParticles, 3);

        particleIndexBuffer.GetData(particleIndicies);
        particleCellIndexBuffer.GetData(particleCellIndicies);
        cellOffsetBuffer.GetData(cellOffsets);

        for (int i = 0; i < numParticles; i++)
        {
            uint particleIdxAt_i = particleIndicies[i];
            uint keyValueAt_i = particleCellIndicies[particleIdxAt_i];
            keyValueDebugger[i] = keyValueAt_i;
        }
    }

    public static void Dispatch(ComputeShader computeShader, int numParticles, int kernelIndex)
    {
        uint x, y, z;
        computeShader.GetKernelThreadGroupSizes(kernelIndex, out x, out y, out z);  // (8, 1, 1)

        int numGroupsX = Mathf.CeilToInt(numParticles / (float)x);
        int numGroupsY = Mathf.CeilToInt(1 / (float)y);
        int numGroupsZ = Mathf.CeilToInt(1 / (float)z);

        computeShader.Dispatch(kernelIndex, numGroupsX, numGroupsY, numGroupsZ);
    }

    float3[] GenerateParticles(int particleCount)
    {
        float3[] points = new float3[particleCount];

        int i = 0;
        for (int x = 0; x < spawnCubeSize.x; x++)
        {
            for (int y = 0; y < spawnCubeSize.y; y++)
            {
                for (int z = 0; z < spawnCubeSize.z; z++)
                {
                    float px = x * offset * 0.5f;
                    float py = y * offset * 0.5f;
                    float pz = z * offset * 0.5f;

                    points[i] = spawnCenter + new float3(px, py, pz);
                    i++;
                }
            }
        }

        return points;
    }

    public void SortPartices()
    {
        var count = numParticles;
        for (var dim = 2; dim <= count; dim <<= 1)
        {
            computeShader.SetInt("dimension", dim);
            for (var block = dim >> 1; block > 0; block >>= 1)
            {
                computeShader.SetInt("block", block);
                Dispatch(computeShader, count, (int)kernels.BitonicSort);
            }
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
