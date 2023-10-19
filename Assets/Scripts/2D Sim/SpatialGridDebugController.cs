using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;

public class SpatialGridDebugController : MonoBehaviour
{
    public static SpatialGridDebugController instance;

    public LayerMask particleLayers;
    public Material mat;
    // Canvas UI
    public GameObject infoPanel;
    public Text infoText;

    [Header("Debug Button")]
    public bool debugEnabled = true;

    private List<GameObject> circleAreaObjList; 

    private void Awake()
    {
        instance = this;
        circleAreaObjList = new List<GameObject>();
    }

    private void Update()
    {
        if (!debugEnabled)
        {
            return;
        }

        infoPanel.SetActive(false);

        // Update particle information
        gameObject.GetComponent<FluidSimulation>().UpdateParticleInfomation();

        // Set particle color to default
        List<GameObject> particleList = gameObject.GetComponent<FluidSimulation>().GetParticleList();
        foreach (GameObject particle in particleList) 
        {
            particle.GetComponent<Renderer>().material.SetColor("_Color", Colour.lightblue);
        }

        if (circleAreaObjList.Count != 0)
        {
            foreach (GameObject obj in circleAreaObjList)
            {
                Destroy(obj);
            }
        }

        RaycastHit hit;
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out hit, particleLayers))
        {
            // Hit with any particle
            infoPanel.SetActive(true);

            ParticleAtt targetParticle = hit.transform.gameObject.GetComponent<ParticleAtt>();
            Material targetParticleMat = hit.transform.gameObject.GetComponent<Renderer>().material;

            infoText.text = targetParticle.PrintParticleInfomation().ToSafeString();
            targetParticleMat.SetColor("_Color", Color.red);

            // Create a area circle for other particles
            DrawAffectArea(targetParticle.position);

            // Loop all particles and set all particles within raidus to blue
            for (int i = 0; i < particleList.Count; i++)
            {
                float dist = (particleList[i].GetComponent<ParticleAtt>().position - targetParticle.position).magnitude;
                if (dist <= gameObject.GetComponent<FluidSimulation>().smoothRadius)
                {
                    particleList[i].GetComponent<Renderer>().material.SetColor("_Color", Color.blue);
                }
            }

            // Use spatial structure to lookup particles within radius
            List<int> particles = gameObject.GetComponent<FluidSimulation>().ForeachPointWithinRadius(targetParticle.position);
            foreach (int index in particles)
            {
                ParticleAtt affectPartice = particleList[index].GetComponent<ParticleAtt>();
                if (affectPartice.GetComponent<Renderer>().material.color == Color.blue)
                {
                    // Visited during all loop then set to green
                    affectPartice.GetComponent<Renderer>().material.SetColor("_Color", Color.green);
                }
                else
                {
                    // Error one set to red
                    affectPartice.GetComponent<Renderer>().material.SetColor("_Color", Color.red);
                }
            }
        }  
    }

    public void DrawGrid(Vector2 screenBound, float radius)
    {
        int numCellsX = (int)(screenBound.x / radius);
        int numCellsY = (int)(screenBound.y / radius);

        float[] offsetX = { 0, radius, radius, 0, 0 };
        float[] offsetY = { 0, 0, radius, radius, 0 };

        List<Vector2> points = new List<Vector2>();
        for (float i = (0 - screenBound.x / 2 - 1); i < numCellsX; i++)
        {
            for (float j = (int)(0 - screenBound.y / 2 - 1); j < numCellsY; j++)
            {
                points.Clear();
                for (int k = 0; k < 5; k++)
                {
                    points.Add(new Vector2(i + offsetX[k], j + offsetY[k]));
                }

                for (int m = 0; m < 4; m++)
                {
                    Gizmos.DrawLine(points[m], points[m + 1]);
                }
            }
        }
    }

    void DrawAffectArea(Vector2 position)
    {
        int radius = (int)gameObject.GetComponent<FluidSimulation>().smoothRadius;
        const int segments = 100;

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

        Material newMat = new Material(mat);

        // Create a new particle object
        GameObject newObj = new GameObject("particle");
        newObj.AddComponent<MeshFilter>();
        newObj.AddComponent<MeshRenderer>();
        newObj.GetComponent<MeshFilter>().mesh = mesh;
        newObj.GetComponent<MeshRenderer>().material = newMat;
        newObj.transform.localPosition = new Vector3(0, 0, 1);

        circleAreaObjList.Add(newObj);
    }
}
