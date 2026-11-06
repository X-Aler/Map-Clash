using System;
using System.IO;

namespace MapClash
{
    public sealed class WorldMapData
    {
        private const uint FileMagic = 0x504D434D;
        private const int FormatVersion = 1;
        private const int HeaderSize = 20;

        public WorldMapData(int width, int height, byte[] terrain)
        {
            if (width <= 0 || height <= 0 || (long)width * height > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "Map dimensions must be positive and fit an array.");
            }

            if (terrain == null || terrain.Length != width * height)
            {
                throw new ArgumentException("Terrain length must match the map dimensions.", nameof(terrain));
            }

            Width = width;
            Height = height;
            Terrain = (byte[])terrain.Clone();
            var landCount = 0;
            for (var index = 0; index < Terrain.Length; index++)
            {
                if (Terrain[index] > 3)
                {
                    throw new ArgumentException("Terrain values must be water, plains, highlands, or mountains.", nameof(terrain));
                }

                if (Terrain[index] != 0)
                {
                    landCount++;
                }
            }

            TotalLandTiles = landCount;
        }

        public int Width { get; }
        public int Height { get; }
        public byte[] Terrain { get; }
        public int TotalLandTiles { get; }
        public double CellScale => 2048.0 / Width;

        public bool IsLand(int index)
        {
            return index >= 0 && index < Terrain.Length && Terrain[index] != 0;
        }

        public int GetNeighbour(int index, int direction)
        {
            if (index < 0 || index >= Terrain.Length)
            {
                return -1;
            }

            var x = index % Width;
            switch (direction)
            {
                case 0:
                    return x == 0 ? index + Width - 1 : index - 1;
                case 1:
                    return x == Width - 1 ? index - Width + 1 : index + 1;
                case 2:
                    return index < Width ? -1 : index - Width;
                case 3:
                    return index >= Terrain.Length - Width ? -1 : index + Width;
                default:
                    throw new ArgumentOutOfRangeException(nameof(direction));
            }
        }

        public static WorldMapData Load(byte[] bytes)
        {
            if (bytes == null || bytes.Length < HeaderSize)
            {
                throw new InvalidDataException("World terrain header is missing.");
            }

            using (var stream = new MemoryStream(bytes, false))
            using (var reader = new BinaryReader(stream))
            {
                if (reader.ReadUInt32() != FileMagic || reader.ReadInt32() != FormatVersion)
                {
                    throw new InvalidDataException("World terrain magic or version is unsupported.");
                }

                var width = reader.ReadInt32();
                var height = reader.ReadInt32();
                var expectedLand = reader.ReadInt32();
                if (width <= 0 || height <= 0 || (long)width * height != bytes.Length - HeaderSize)
                {
                    throw new InvalidDataException("World terrain dimensions do not match the payload.");
                }

                var map = new WorldMapData(width, height, reader.ReadBytes(bytes.Length - HeaderSize));
                if (map.TotalLandTiles != expectedLand)
                {
                    throw new InvalidDataException("World terrain land count does not match the header.");
                }

                return map;
            }
        }
    }
}
