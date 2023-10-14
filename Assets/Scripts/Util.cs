using System;
using UnityEngine;

public class Colour
{
    public static Color lightblue
    {
        get
        {
            return new Color(0.68f, 0.85f, 0.9f, 1.0f);
        }
    }

    public static Color lightgreen
    {
        get
        {
            return new Color(0.56f, 0.93f, 0.56f, 1.0f);
        }
    }

    // #CBC3E3
    public static Color lightpurple
    {
        get
        {
            return new Color(0.8f, 0.76f, 0.89f, 1.0f);
        }
    }
}

public class Entry : IComparable
{
    public int index;                   // the index in the points array
    public uint hashKey;                // the hashkey of the given points

    public Entry(int _index, uint _hashKey)
    {
        this.index = _index;
        this.hashKey = _hashKey;
    }

    // Customized class compare function
    public int CompareTo(object obj)
    {
        Entry entry = obj as Entry;
        return this.hashKey.CompareTo(entry.hashKey);
    }
}

public enum ParticlePattern
{ 
    Random,
    Uniform
}