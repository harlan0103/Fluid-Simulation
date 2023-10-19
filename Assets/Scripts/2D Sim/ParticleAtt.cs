using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ParticleAtt : MonoBehaviour
{
    public int particleIdx;             // particle index in positions list
    public Vector2 position;            // particle position in world space;
    public uint hashKey;                // particle hash key value 
    public int cellX;                   // cell coordinate x value
    public int cellY;                   // cell coordinate y value

    public void UpdateParticleInfo(int particleIdx, Vector2 position, uint hashKey, int cellX, int cellY)
    {
        this.particleIdx = particleIdx;
        this.position = position;
        this.hashKey = hashKey;
        this.cellX = cellX;
        this.cellY = cellY;
    }

    public string PrintParticleInfomation()
    {
        string info = "";
        info += "particle " + particleIdx + " at position: " + position + "\n";
        info += "cell coordinate: ( " + cellX + ", " + cellY + " ) with hashKey: " + hashKey + "\n";

        return info;
    }
}
