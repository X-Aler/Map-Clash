using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace MapClash.Tests
{
    public sealed class MapDataTests
    {
        [Test]
        public void NeighboursWrapLongitudeButNeverLatitude()
        {
            var map = new WorldMapData(4, 2, new byte[8]);

            Assert.That(map.GetNeighbour(0, 0), Is.EqualTo(3));
            Assert.That(map.GetNeighbour(3, 1), Is.EqualTo(0));
            Assert.That(map.GetNeighbour(4, 0), Is.EqualTo(7));
            Assert.That(map.GetNeighbour(7, 1), Is.EqualTo(4));
            Assert.That(map.GetNeighbour(0, 2), Is.EqualTo(-1));
            Assert.That(map.GetNeighbour(7, 3), Is.EqualTo(-1));
            Assert.That(map.GetNeighbour(0, 3), Is.EqualTo(4));
            Assert.That(map.GetNeighbour(4, 2), Is.EqualTo(0));
            Assert.That(map.GetNeighbour(-1, 0), Is.EqualTo(-1));
        }

        [Test]
        public void LandCountExcludesWaterAndCellScaleUsesBaseGrid()
        {
            var map = new WorldMapData(4, 2, new byte[] { 0, 1, 2, 3, 0, 0, 1, 0 });

            Assert.That(map.TotalLandTiles, Is.EqualTo(4));
            Assert.That(map.CellScale, Is.EqualTo(512));
            Assert.That(map.IsLand(-1), Is.False);
            Assert.That(map.IsLand(8), Is.False);
            Assert.That(map.IsLand(0), Is.False);
            Assert.That(map.IsLand(3), Is.True);
        }

        [Test]
        public void ConstructorRejectsBrokenMapData()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new WorldMapData(0, 2, new byte[0]));
            Assert.Throws<ArgumentException>(() => new WorldMapData(2, 2, new byte[3]));
            Assert.Throws<ArgumentException>(() => new WorldMapData(1, 1, new byte[] { 4 }));
            Assert.Throws<InvalidDataException>(() => WorldMapData.Load(new byte[20]));
        }

        [Test]
        public void RuntimeResourceHasExpectedDimensionsAndGeography()
        {
            var resource = Resources.Load<TextAsset>("WorldTerrain");
            Assert.That(resource, Is.Not.Null, "WorldTerrain must be present in the built player.");
            var map = WorldMapData.Load(resource.bytes);

            Assert.That(map.Width, Is.EqualTo(2048));
            Assert.That(map.Height, Is.EqualTo(1024));
            Assert.That(map.TotalLandTiles, Is.EqualTo(688403));
            Assert.That(map.CellScale, Is.EqualTo(1));
            Assert.That(map.IsLand(Cell(map, 0, -85)), Is.True, "Antarctica is part of the map.");
            Assert.That(map.IsLand(Cell(map, -2, 54)), Is.True, "Britain remains land.");
            Assert.That(map.IsLand(Cell(map, 138, 37)), Is.True, "Japan remains land.");
            Assert.That(map.IsLand(Cell(map, 170, -44)), Is.True, "New Zealand remains land.");
            Assert.That(map.IsLand(Cell(map, -168.8, 65.7)), Is.False, "Bering Strait remains water.");
            Assert.That(map.IsLand(Cell(map, -5.6, 35.95)), Is.False, "Gibraltar remains water.");
            Assert.That(map.IsLand(Cell(map, 1.5, 51)), Is.False, "The English Channel remains water.");
            Assert.That(map.IsLand(Cell(map, 33, -1)), Is.False, "Lake Victoria is excluded from land.");
            Assert.That(map.IsLand(Cell(map, 51, 42)), Is.False, "The Caspian Sea is excluded from land.");
            Assert.That(map.Terrain[Cell(map, 86, 29)], Is.EqualTo(3), "Himalayas use real ETOPO heights.");
        }

        [Test]
        public void LoaderRejectsTruncationAndIncorrectLandCount()
        {
            var resource = Resources.Load<TextAsset>("WorldTerrain");
            Assert.That(resource, Is.Not.Null);
            var broken = (byte[])resource.bytes.Clone();
            broken[16] ^= 1;

            Assert.Throws<InvalidDataException>(() => WorldMapData.Load(broken));
            Array.Resize(ref broken, broken.Length - 1);
            Assert.Throws<InvalidDataException>(() => WorldMapData.Load(broken));
        }

        private static int Cell(WorldMapData map, double longitude, double latitude)
        {
            var x = (int)((longitude + 180) / 360 * map.Width);
            var y = (int)((latitude + 90) / 180 * map.Height);
            return y * map.Width + x;
        }
    }
}
