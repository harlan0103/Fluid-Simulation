using System.Collections;
using Unity.Mathematics;
using UnityEngine;

public class BoundingBoxController : MonoBehaviour
{
    public GameObject ground;

    private void Start()
    {
        StartCoroutine(RotateAround());
    }

    // Function to draw the bounding box using Gizmos
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.blue;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
    }

    private void RotationProcedural()
    { 
        // TODO: Add rotation procedural
        // -Z: 45, Y: 60, X: 90, Z: 60
    }

    public Vector3 GetBoundingBox()
    {
        return gameObject.transform.localScale;
    }

    IEnumerator RotateAround()
    {
        yield return new WaitForSeconds(5f);

        StartCoroutine(RotateAroundAxisWithDegree(-45, Vector3.forward, 3f));

        yield return new WaitForSeconds(5f);

        StartCoroutine(RotateAroundAxisWithDegree(60, Vector3.up, 3f));

        yield return new WaitForSeconds(5f);

        StartCoroutine(RotateAroundAxisWithDegree(45, Vector3.forward, 3f));

        yield return new WaitForSeconds(5f);

        StartCoroutine(RotateAroundAxisWithDegree(180, Vector3.up, 3f));

        yield return new WaitForSeconds(5f);

        StartCoroutine(RotateAroundAxisWithDegree(180, Vector3.back, 3f));
    }

    IEnumerator RotateAroundAxisWithDegree(float degree, Vector3 axis, float rotationTime)
    {
        Quaternion currentRotation = transform.rotation;
        Quaternion targetRotation = Quaternion.AngleAxis(degree, axis);
        float startTime = Time.time;

        while (Time.time - startTime < rotationTime)
        {
            float t = (Time.time - startTime) / rotationTime;
            transform.rotation = Quaternion.Slerp(currentRotation, targetRotation, t);
            yield return null;
        }

        transform.rotation = targetRotation;
    }

}
