using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class FluidSimulation : MonoBehaviour
{
    private static int segments = 100;

    public Material mat;
    public LineRenderer boundingBoxRenderer;

    public int numParticles = 1;

    [Range(0.05f, 5.0f)]
    public float particleSize = 0.1f;
    public int gravity = 5;
    [Range(0.4f, 1.0f)]
    public float collisionDamping = 0.8f;
    [Range(0.0f, 3.0f)]
    public float particleSpacing = 1.0f;

    private List<GameObject> particleList;

    private Vector2 boundSize;
    private Vector2[] positions;
    private Vector2[] velocities;
    private float[] particleProperties;

    // Start is called before the first frame update
    void Start()
    {
        particleList = new List<GameObject>();

        // Initialize 
        boundSize = CalculateViewPortSize();
        DrawBoundary();

        // Uniform generate particles
        //RandomCreateParticles(1024);
        UniformCreateParticles();

        // Draw particles on the screen
        UpdateParticleMovement();
    }

    // Update is called once per frame
    void Update()
    {
        UpdateParticleMovement();
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

    void RandomCreateParticles(int seed)
    {
        System.Random rng = new(seed);
        positions = new Vector2[numParticles];
        particleProperties = new float[numParticles];
        velocities = new Vector2[numParticles];

        for (int i = 0; i < positions.Length; i++)
        {
            float x = (float)(rng.NextDouble() - 0.5) * boundSize.x;
            float y = (float)(rng.NextDouble() - 0.5) * boundSize.y;

            positions[i] = new Vector2(x, y);
        }
    }

    void UniformCreateParticles()
    {
        // Create particle arrays
        positions = new Vector2[numParticles];
        velocities = new Vector2[numParticles];
        particleProperties = new float[numParticles];

        // Place particles in a grid formation
        int particlesPerRow = (int)Mathf.Sqrt(numParticles);
        int particlesPerCol = (numParticles - 1) / particlesPerRow + 1;
        float spacing = particleSize * 2 + particleSpacing;

        for (int i = 0; i < numParticles; i++)
        {
            float x = (i % particlesPerRow - particlesPerRow / 2f + 0.5f) * spacing;
            float y = (i / particlesPerRow - particlesPerCol / 2f + 0.5f) * spacing;
            positions[i] = new Vector2(x, y);
        }
    }

    void UpdateParticleMovement()
    {
        // Delete all existing particles first
        foreach (Transform trans in transform)
        {
            particleList.Add(trans.gameObject);
        }
        foreach (GameObject particle in particleList)
        {
            Destroy(particle);
        }

        for (int i = 0; i < positions.Length; i++)
        {
            ResolveCollisions(ref positions[i], ref velocities[i]);
            velocities[i] += Vector2.down * gravity * Time.deltaTime;
            positions[i] += velocities[i] * Time.deltaTime;
        }

        for (int i = 0; i < positions.Length; i++)
        {
            DrawCircle(positions[i], particleSize, Colour.lightblue);
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

        mat.SetColor("_Color", colour);

        // Create a new particle object
        GameObject newObj = new GameObject("particle");
        newObj.AddComponent<MeshFilter>();
        newObj.AddComponent<MeshRenderer>();
        newObj.GetComponent<MeshFilter>().mesh = mesh;
        newObj.GetComponent<MeshRenderer>().material = mat;

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
}
