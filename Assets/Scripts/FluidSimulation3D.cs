using Unity.Mathematics;
using UnityEngine;

public class FluidSimulation3D : MonoBehaviour
{
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

    // Compute shader and buffers
    private ComputeShader computeShader;
    private ComputeBuffer positionBuffer;

    // Compute data arrays
    private float3[] computePositions;

    void Start()
    {
        InitializeComputeBuffers();
    }

    void Update()
    {
        UpdateSettings(Time.deltaTime);
        RunSimulationStep();
    }

    void LateUpdate()
    {
        particleMat.SetFloat("_Scale", particleSize);
        Graphics.DrawMeshInstancedIndirect(mesh, subMeshIndex, particleMat, new Bounds(Vector3.zero, new Vector3(100.0f, 100.0f, 100.0f)), argsBuffer);
    }

    void InitializeComputeBuffers()
    {
        computeShader = Resources.Load<ComputeShader>("FluidSim3D");

        // Initialize Buffers
        positionBuffer = new ComputeBuffer(numParticles, sizeof(float) * 3);

        instanceCount = numParticles;
        argsBuffer = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);

        // Initialize data arrays
        computePositions = new float3[numParticles];

        // Initialize particles
        computePositions[0] = new float3(0f, 0f, 0f);

        // Set data arrays to buffers
        positionBuffer.SetData(computePositions);

        // Initialize compute buffers
        computeShader.SetBuffer(0, "Positions", positionBuffer);

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
    }

    void UpdateSettings(float deltaTime)
    {
        computeShader.SetInt("numParticles", numParticles);
        computeShader.SetFloat("deltaTime", deltaTime);
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
}
