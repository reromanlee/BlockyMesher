using System.Collections.Generic;
using NUnit.Framework;
using reromanlee.BlockyMesher.Meshing;
using Unity.Mathematics;
using UnityEngine;
using static reromanlee.BlockyMesher.Tests.TestBlocks;

namespace reromanlee.BlockyMesher.Tests
{
    public class LightingTests
    {
        BuildHarness world;

        [SetUp] public void SetUp() => world = new BuildHarness();
        [TearDown] public void TearDown() => world.Dispose();

        [Test]
        public void OpenSkyIsFullyLit()
        {
            world.Fill(new int3(0, 0, 0), new int3(16, 1, 16), Stone);
            world.Run();
            Assert.AreEqual(LightJob.MaxLevel, world.SkyAt(5, 1, 5));
            Assert.AreEqual(LightJob.MaxLevel, world.SkyAt(5, 15, 5));
        }

        [Test]
        public void ASealedRoomStaysDark()
        {
            world.Fill(new int3(2, 2, 2), new int3(9, 9, 9), Stone);
            world.Fill(new int3(3, 3, 3), new int3(8, 8, 8), Air);
            world.Run();
            Assert.AreEqual(0, world.SkyAt(5, 5, 5));
        }

        [Test]
        public void LightFadesOneLevelPerBlockDownATunnel()
        {
            // A solid slab with a tunnel starting at x = 0. The open air at x = -1 is in the next
            // column, so this also checks that light crosses from one column into another.
            world.Fill(new int3(0, 4, 0), new int3(16, 10, 16), Stone);
            world.Fill(new int3(0, 5, 5), new int3(12, 6, 6), Air);
            world.Run();
            Assert.AreEqual(7, world.SkyAt(-1, 5, 5));
            for (int x = 0; x < 7; x++)
                Assert.AreEqual(6 - x, world.SkyAt(x, 5, 5), $"x = {x}");
            Assert.AreEqual(0, world.SkyAt(8, 5, 5));
        }

        [Test]
        public void SolidBlocksReceiveLightButDoNotPassItOn()
        {
            world.Fill(new int3(0, 4, 0), new int3(16, 10, 16), Stone);
            world.Fill(new int3(0, 5, 5), new int3(12, 6, 6), Air);
            world.Run();
            Assert.AreEqual(4, world.SkyAt(1, 6, 5), "the tunnel roof above x = 1 is lit by the tunnel");
            Assert.AreEqual(0, world.SkyAt(1, 7, 5), "but doesn't light the stone above it");
        }

        [Test]
        public void CappingAShaftDarkensIt()
        {
            world.Fill(new int3(0, 0, 0), new int3(16, 10, 16), Stone);
            world.Fill(new int3(5, 1, 5), new int3(6, 10, 6), Air);
            world.Run();
            Assert.AreEqual(7, world.SkyAt(5, 1, 5));

            world.Set(5, 9, 5, Stone);
            world.Run();
            Assert.AreEqual(0, world.SkyAt(5, 1, 5));
        }

        [Test]
        public void GlassLetsSkylightThroughButLeavesDoNot()
        {
            world.Fill(new int3(0, 0, 0), new int3(16, 10, 16), Stone);
            world.Fill(new int3(5, 1, 5), new int3(6, 10, 6), Air);
            world.Fill(new int3(9, 1, 9), new int3(10, 10, 10), Air);
            world.Set(5, 9, 5, Glass);
            world.Set(9, 9, 9, Leaves);
            world.Run();
            Assert.AreEqual(7, world.SkyAt(5, 1, 5));
            Assert.AreEqual(0, world.SkyAt(9, 1, 9));
        }

        [Test]
        public void BlockLightCarriesItsColorAndTheBrightestWins()
        {
            world.Fill(new int3(0, 0, 0), new int3(16, 8, 16), Stone);
            world.Fill(new int3(1, 1, 1), new int3(15, 7, 15), Air);
            world.Set(1, 3, 3, RedLamp);
            world.Set(14, 3, 3, BlueLamp);
            world.Run();

            int red = world.Blocks.Table[RedLamp].LightColor;
            int blue = world.Blocks.Table[BlueLamp].LightColor;
            Assert.AreEqual(6, world.BlockLightAt(2, 3, 3));
            Assert.AreEqual(red, world.LightColorAt(2, 3, 3));
            Assert.AreEqual(4, world.BlockLightAt(13, 3, 3));
            Assert.AreEqual(blue, world.LightColorAt(13, 3, 3));
            Assert.AreEqual(2, world.BlockLightAt(6, 3, 3), "red reaches 5 blocks away at level 2");
            Assert.AreEqual(red, world.LightColorAt(6, 3, 3));
            Assert.AreEqual(0, world.SkyAt(7, 3, 3), "the room is sealed from the sky");
        }

        [Test]
        public void FacesSharingACornerLightItAlikeWhereColorsMeet()
        {
            // Red reaches x = 7 at level 3 and blue reaches x = 8 at level 3: the two meet in a tie.
            world.Fill(new int3(0, 0, 0), new int3(16, 1, 16), Stone);
            world.Set(3, 1, 8, RedLamp);
            world.Set(10, 1, 8, BlueLamp);
            world.Run();
            Assert.AreEqual(3, world.BlockLightAt(7, 1, 8));
            Assert.AreEqual(3, world.BlockLightAt(8, 1, 8));

            var corners = new Dictionary<int3, Color32>();
            foreach (SectionVertex[] face in world.FacesAt(v => v.Y == 1))
            foreach (SectionVertex corner in face)
            {
                var position = new int3(corner.X, corner.Y, corner.Z);
                if (corners.TryGetValue(position, out Color32 other))
                    Assert.AreEqual(other, corner.BlockLight, $"floor faces disagree on the light at ({position.x}, {position.z})");
                else
                    corners.Add(position, corner.BlockLight);
            }
            Color32 meeting = corners[new int3(8, 1, 8)];
            Assert.That(meeting.r > 0 && meeting.b > 0, $"red and blue mix where they meet, got {meeting}");
        }

        [Test]
        public void SmoothLightingBrightensCornersTowardTheLight()
        {
            world.Fill(new int3(0, 4, 0), new int3(16, 10, 16), Stone);
            world.Fill(new int3(0, 5, 5), new int3(12, 6, 6), Air);
            world.Run();

            // Floor of the tunnel under x = 1: its front block has level 5, the one toward the entrance 6.
            SectionVertex[] floor = world.FacesAt(v => v.Y == 5 && v.X >= 1 && v.X <= 2 && v.Z >= 5 && v.Z <= 6)[0];
            foreach (SectionVertex corner in floor)
                Assert.AreEqual(corner.X == 1 ? 6 : 5, corner.Sky, $"corner at x = {corner.X}");

            world.Settings.SmoothLighting = false;
            world.Run();
            floor = world.FacesAt(v => v.Y == 5 && v.X >= 1 && v.X <= 2 && v.Z >= 5 && v.Z <= 6)[0];
            foreach (SectionVertex corner in floor)
                Assert.AreEqual(5, corner.Sky);
        }

        [Test]
        public void LightDoesNotLeakThroughTheCornerOfTwoWalls()
        {
            // A dark one-block cell on an open floor. The blocks diagonal to it are lit, but they only
            // touch its floor corners through the corner where two walls meet.
            world.Fill(new int3(0, 0, 0), new int3(16, 1, 16), Stone);
            world.Set(4, 1, 5, Stone);
            world.Set(6, 1, 5, Stone);
            world.Set(5, 1, 4, Stone);
            world.Set(5, 1, 6, Stone);
            world.Set(5, 2, 5, Stone);
            world.Run();
            Assert.AreEqual(0, world.SkyAt(5, 1, 5));
            Assert.AreEqual(7, world.SkyAt(6, 1, 6));

            SectionVertex[] floor = world.FacesAt(v => v.Y == 1 && v.X >= 5 && v.X <= 6 && v.Z >= 5 && v.Z <= 6)[0];
            foreach (SectionVertex corner in floor)
                Assert.AreEqual(0, corner.Sky, $"corner at ({corner.X}, {corner.Z})");
        }

        [Test]
        public void ConnectionTableIsAFloodFillFromTheFrontBlock()
        {
            // Blocks around a corner: 0 front, then front+U, front+V, own+U, own+V, front+U+V, own+U+V.
            int[][] touching =
            {
                new[] { 1, 2 }, new[] { 0, 3, 5 }, new[] { 0, 4, 5 }, new[] { 1, 6 }, new[] { 2, 6 }, new[] { 1, 2, 6 }, new[] { 3, 4, 5 },
            };
            for (int passable = 0; passable < 64; passable++)
            {
                var reached = new HashSet<int> { 0 };
                var queue = new Queue<int>(reached);
                while (queue.Count > 0)
                foreach (int next in touching[queue.Dequeue()])
                    if (next != 0 && (passable >> (next - 1) & 1) != 0 && reached.Add(next)) queue.Enqueue(next);

                int expected = 0;
                for (int block = 1; block <= 6; block++)
                    if (reached.Contains(block)) expected |= 1 << (block - 1);
                Assert.AreEqual(expected, LightConnections.Table[passable], $"passable = {passable}");
            }
        }

        [Test]
        public void ConnectionTableMatchesThePrototypesHandMadeTable()
        {
            // vertexLightmapConnections from the prototype's Tables.cs, verbatim. Its index packs
            // B2..D7 (front+U, front+V, own+U, own+V, front+U+V, own+U+V) from bit 5 down to bit 0.
            int[][] prototype =
            {
                new int[0], new int[0], new int[0], new int[0], new int[0], new int[0], new int[0], new int[0],
                new int[0], new int[0], new int[0], new int[0], new int[0], new int[0], new int[0], new int[0],
                new[] { 2 }, new[] { 2 }, new[] { 2, 5 }, new[] { 2, 5, 6 }, new[] { 2, 4 }, new[] { 2, 4, 6 }, new[] { 2, 5, 4 }, new[] { 2, 5, 4, 6 },
                new[] { 2 }, new[] { 2 }, new[] { 2, 5 }, new[] { 2, 5, 6, 3 }, new[] { 2, 4 }, new[] { 2, 4, 6, 3 }, new[] { 2, 5, 4 }, new[] { 2, 5, 6, 4, 3 },
                new[] { 1 }, new[] { 1 }, new[] { 1, 5 }, new[] { 1, 5, 6 }, new[] { 1 }, new[] { 1 }, new[] { 1, 5 }, new[] { 1, 5, 6, 4 },
                new[] { 1, 3 }, new[] { 1, 3, 6 }, new[] { 1, 3, 5 }, new[] { 1, 3, 5, 6 }, new[] { 1, 3 }, new[] { 1, 3, 6, 4 }, new[] { 1, 3, 5 }, new[] { 1, 3, 5, 6, 4 },
                new[] { 1, 2 }, new[] { 1, 2 }, new[] { 1, 2, 5 }, new[] { 1, 2, 5, 6 }, new[] { 1, 2, 4 }, new[] { 1, 2, 4, 6 }, new[] { 1, 2, 4, 5 }, new[] { 1, 2, 4, 5, 6 },
                new[] { 1, 2, 3 }, new[] { 1, 2, 3, 6 }, new[] { 1, 2, 3, 5 }, new[] { 1, 2, 3, 5, 6 }, new[] { 1, 2, 3, 4 }, new[] { 1, 2, 3, 4, 6 }, new[] { 1, 2, 3, 4, 5 }, new[] { 1, 2, 3, 4, 5, 6 },
            };
            for (int index = 0; index < 64; index++)
            {
                int passable = 0;
                for (int block = 1; block <= 6; block++)
                    if ((index >> (6 - block) & 1) != 0) passable |= 1 << (block - 1);
                int expected = 0;
                foreach (int block in prototype[index])
                    expected |= 1 << (block - 1);
                Assert.AreEqual(expected, LightConnections.Table[passable], $"prototype index {index}");
            }
        }
    }
}
