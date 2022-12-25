using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ValveResourceFormat.Compression;
using ValveResourceFormat.Serialization;
using System.Linq;
using System.Runtime.InteropServices;

namespace ValveResourceFormat.Blocks
{
    /// <summary>
    /// "VBIB" block.
    /// </summary>
    public class VBIB : Block
    {
        public override BlockType Type => BlockType.VBIB;

        public List<OnDiskBufferData> VertexBuffers { get; }
        public List<OnDiskBufferData> IndexBuffers { get; }

#pragma warning disable CA1051 // Do not declare visible instance fields
        public struct OnDiskBufferData
        {
            public uint ElementCount;
            //stride for vertices. Type for indices
            public uint ElementSizeInBytes;
            //Vertex attribs. Empty for index buffers
            public RenderInputLayoutField[] InputLayoutFields;
            public byte[] Data;
        }

        public struct RenderInputLayoutField
        {
            public string SemanticName;
            public int SemanticIndex;
            public DXGI_FORMAT Format;
            public uint Offset;
            public int Slot;
            public RenderSlotType SlotType;
            public int InstanceStepRate;
        }
#pragma warning restore CA1051 // Do not declare visible instance fields

        public VBIB()
        {
            VertexBuffers = new List<OnDiskBufferData>();
            IndexBuffers = new List<OnDiskBufferData>();
        }

        public VBIB(IKeyValueCollection data) : this()
        {
            var vertexBuffers = data.GetArray("m_vertexBuffers");
            foreach (var vb in vertexBuffers)
            {
                var vertexBuffer = BufferDataFromDATA(vb);

                var decompressedSize = vertexBuffer.ElementCount * vertexBuffer.ElementSizeInBytes;
                if (vertexBuffer.Data.Length != decompressedSize)
                {
                    vertexBuffer.Data = MeshOptimizerVertexDecoder.DecodeVertexBuffer((int)vertexBuffer.ElementCount, (int)vertexBuffer.ElementSizeInBytes, vertexBuffer.Data);
                }
                FixupVertexBuffer(ref vertexBuffer);
                VertexBuffers.Add(vertexBuffer);
            }
            var indexBuffers = data.GetArray("m_indexBuffers");
            foreach (var ib in indexBuffers)
            {
                var indexBuffer = BufferDataFromDATA(ib);

                var decompressedSize = indexBuffer.ElementCount * indexBuffer.ElementSizeInBytes;
                if (indexBuffer.Data.Length != decompressedSize)
                {
                    indexBuffer.Data = MeshOptimizerIndexDecoder.DecodeIndexBuffer((int)indexBuffer.ElementCount, (int)indexBuffer.ElementSizeInBytes, indexBuffer.Data);
                }

                IndexBuffers.Add(indexBuffer);
            }
        }

        public override void Read(BinaryReader reader, Resource resource)
        {
            reader.BaseStream.Position = Offset;

            var vertexBufferOffset = reader.ReadUInt32();
            var vertexBufferCount = reader.ReadUInt32();
            var indexBufferOffset = reader.ReadUInt32();
            var indexBufferCount = reader.ReadUInt32();

            reader.BaseStream.Position = Offset + vertexBufferOffset;
            for (var i = 0; i < vertexBufferCount; i++)
            {
                var vertexBuffer = ReadOnDiskBufferData(reader);

                var decompressedSize = vertexBuffer.ElementCount * vertexBuffer.ElementSizeInBytes;
                if (vertexBuffer.Data.Length != decompressedSize)
                {
                    vertexBuffer.Data = MeshOptimizerVertexDecoder.DecodeVertexBuffer((int)vertexBuffer.ElementCount, (int)vertexBuffer.ElementSizeInBytes, vertexBuffer.Data);
                }

                FixupVertexBuffer(ref vertexBuffer);
                VertexBuffers.Add(vertexBuffer);
            }

            reader.BaseStream.Position = Offset + 8 + indexBufferOffset; //8 to take into account vertexOffset / count
            for (var i = 0; i < indexBufferCount; i++)
            {
                var indexBuffer = ReadOnDiskBufferData(reader);

                var decompressedSize = indexBuffer.ElementCount * indexBuffer.ElementSizeInBytes;
                if (indexBuffer.Data.Length != decompressedSize)
                {
                    indexBuffer.Data = MeshOptimizerIndexDecoder.DecodeIndexBuffer((int)indexBuffer.ElementCount, (int)indexBuffer.ElementSizeInBytes, indexBuffer.Data);
                }

                IndexBuffers.Add(indexBuffer);
            }
        }

        private static OnDiskBufferData ReadOnDiskBufferData(BinaryReader reader)
        {
            var buffer = default(OnDiskBufferData);

            buffer.ElementCount = reader.ReadUInt32();            //0
            buffer.ElementSizeInBytes = reader.ReadUInt32();      //4

            var refA = reader.BaseStream.Position;
            var attributeOffset = reader.ReadUInt32();  //8
            var attributeCount = reader.ReadUInt32();   //12

            var refB = reader.BaseStream.Position;
            var dataOffset = reader.ReadUInt32();       //16
            var totalSize = reader.ReadInt32();        //20

            reader.BaseStream.Position = refA + attributeOffset;
            buffer.InputLayoutFields = Enumerable.Range(0, (int)attributeCount)
                .Select(j =>
                {
                    var attribute = default(RenderInputLayoutField);

                    var previousPosition = reader.BaseStream.Position;
                    attribute.SemanticName = reader.ReadNullTermString(Encoding.UTF8).ToUpperInvariant();
                    reader.BaseStream.Position = previousPosition + 32; //32 bytes long null-terminated string

                    attribute.SemanticIndex = reader.ReadInt32();
                    attribute.Format = (DXGI_FORMAT)reader.ReadUInt32();
                    attribute.Offset = reader.ReadUInt32();
                    attribute.Slot = reader.ReadInt32();
                    attribute.SlotType = (RenderSlotType)reader.ReadUInt32();
                    attribute.InstanceStepRate = reader.ReadInt32();

                    return attribute;
                })
                .ToArray();

            reader.BaseStream.Position = refB + dataOffset;

            buffer.Data = reader.ReadBytes(totalSize); //can be compressed

            reader.BaseStream.Position = refB + 8; //Go back to the index array to read the next iteration.

            return buffer;
        }

        private static OnDiskBufferData BufferDataFromDATA(IKeyValueCollection data)
        {
            var buffer = new OnDiskBufferData
            {
                ElementCount = data.GetUInt32Property("m_nElementCount"),
                ElementSizeInBytes = data.GetUInt32Property("m_nElementSizeInBytes"),
            };

            var inputLayoutFields = data.GetArray("m_inputLayoutFields");
            buffer.InputLayoutFields = inputLayoutFields.Select(il => new RenderInputLayoutField
            {
                //null-terminated string
                SemanticName = Encoding.UTF8.GetString(il.GetArray<byte>("m_pSemanticName")).TrimEnd((char)0),
                SemanticIndex = il.GetInt32Property("m_nSemanticIndex"),
                Format = (DXGI_FORMAT)il.GetUInt32Property("m_Format"),
                Offset = il.GetUInt32Property("m_nOffset"),
                Slot = il.GetInt32Property("m_nSlot"),
                SlotType = (RenderSlotType)il.GetUInt32Property("m_nSlotType"),
                InstanceStepRate = il.GetInt32Property("m_nInstanceStepRate")
            }).ToArray();

            buffer.Data = data.GetArray<byte>("m_pData");

            return buffer;
        }

        public static float[] ReadVertexAttribute(int offset, OnDiskBufferData vertexBuffer, RenderInputLayoutField attribute)
        {
            float[] result;

            offset = (int)(offset * vertexBuffer.ElementSizeInBytes) + (int)attribute.Offset;

            // Useful reference: https://github.com/apitrace/dxsdk/blob/master/Include/d3dx_dxgiformatconvert.inl
            switch (attribute.Format)
            {
                case DXGI_FORMAT.R32G32B32_FLOAT:
                    {
                        result = new float[3];
                        Buffer.BlockCopy(vertexBuffer.Data, offset, result, 0, 12);
                        break;
                    }

                case DXGI_FORMAT.R32G32B32A32_FLOAT:
                    {
                        result = new float[4];
                        Buffer.BlockCopy(vertexBuffer.Data, offset, result, 0, 16);
                        break;
                    }

                case DXGI_FORMAT.R16G16_UNORM:
                    {
                        var shorts = new ushort[2];
                        Buffer.BlockCopy(vertexBuffer.Data, offset, shorts, 0, 4);

                        result = new[]
                        {
                            (float)shorts[0] / ushort.MaxValue,
                            (float)shorts[1] / ushort.MaxValue,
                        };
                        break;
                    }

                case DXGI_FORMAT.R16G16_SNORM:
                    {
                        var shorts = new short[2];
                        Buffer.BlockCopy(vertexBuffer.Data, offset, shorts, 0, 4);

                        result = new[]
                        {
                            (float)shorts[0] / short.MaxValue,
                            (float)shorts[1] / short.MaxValue,
                        };
                        break;
                    }

                case DXGI_FORMAT.R16G16_FLOAT:
                    {
                        result = new[]
                        {
                            (float)BitConverter.ToHalf(vertexBuffer.Data, offset),
                            (float)BitConverter.ToHalf(vertexBuffer.Data, offset + 2),
                        };
                        break;
                    }

                case DXGI_FORMAT.R32_FLOAT:
                    {
                        result = new float[1];
                        Buffer.BlockCopy(vertexBuffer.Data, offset, result, 0, 4);
                        break;
                    }

                case DXGI_FORMAT.R32G32_FLOAT:
                    {
                        result = new float[2];
                        Buffer.BlockCopy(vertexBuffer.Data, offset, result, 0, 8);
                        break;
                    }

                case DXGI_FORMAT.R16G16_SINT:
                    {
                        var shorts = new short[2];
                        Buffer.BlockCopy(vertexBuffer.Data, offset, shorts, 0, 4);

                        result = new float[2];
                        for (var i = 0; i < 2; i++)
                        {
                            result[i] = shorts[i];
                        }

                        break;
                    }

                case DXGI_FORMAT.R16G16B16A16_SINT:
                    {
                        var shorts = new short[4];
                        Buffer.BlockCopy(vertexBuffer.Data, offset, shorts, 0, 8);

                        result = new float[4];
                        for (var i = 0; i < 4; i++)
                        {
                            result[i] = shorts[i];
                        }

                        break;
                    }

                case DXGI_FORMAT.R16G16B16A16_UINT:
                case DXGI_FORMAT.R16G16B16A16_UNORM:
                    {
                        var shorts = new ushort[4];
                        Buffer.BlockCopy(vertexBuffer.Data, offset, shorts, 0, 8);

                        result = new float[4];
                        for (var i = 0; i < 4; i++)
                        {
                            result[i] = attribute.Format == DXGI_FORMAT.R16G16B16A16_UNORM
                                ? (float)shorts[i] / ushort.MaxValue
                                : shorts[i];
                        }

                        break;
                    }

                case DXGI_FORMAT.R8G8B8A8_UINT:
                case DXGI_FORMAT.R8G8B8A8_UNORM:
                    {
                        var bytes = new byte[4];
                        Buffer.BlockCopy(vertexBuffer.Data, offset, bytes, 0, 4);

                        result = new float[4];
                        for (var i = 0; i < 4; i++)
                        {
                            result[i] = attribute.Format == DXGI_FORMAT.R8G8B8A8_UNORM
                                ? (float)bytes[i] / byte.MaxValue
                                : bytes[i];
                        }

                        break;
                    }

                case DXGI_FORMAT.R16G16B16A16_FLOAT:
                    {
                        result = new[]
                        {
                            (float)BitConverter.ToHalf(vertexBuffer.Data, offset),
                            (float)BitConverter.ToHalf(vertexBuffer.Data, offset + 2),
                            (float)BitConverter.ToHalf(vertexBuffer.Data, offset + 4),
                            (float)BitConverter.ToHalf(vertexBuffer.Data, offset + 6),
                        };
                        break;
                    }

                default:
                    throw new NotImplementedException($"Unsupported \"{attribute.SemanticName}\" DXGI_FORMAT.{attribute.Format}");
            }

            return result;
        }

        public override void WriteText(IndentedTextWriter writer)
        {
            writer.WriteLine("Vertex buffers:");

            foreach (var vertexBuffer in VertexBuffers)
            {
                writer.WriteLine($"Count: {vertexBuffer.ElementCount}");
                writer.WriteLine($"Size: {vertexBuffer.ElementSizeInBytes}");

                for (var i = 0; i < vertexBuffer.InputLayoutFields.Length; i++)
                {
                    var vertexAttribute = vertexBuffer.InputLayoutFields[i];
                    writer.WriteLine($"Attribute[{i}]");
                    writer.Indent++;
                    writer.WriteLine($"SemanticName = {vertexAttribute.SemanticName}");
                    writer.WriteLine($"SemanticIndex = {vertexAttribute.SemanticIndex}");
                    writer.WriteLine($"Offset = {vertexAttribute.Offset}");
                    writer.WriteLine($"Format = {vertexAttribute.Format}");
                    writer.WriteLine($"Slot = {vertexAttribute.Slot}");
                    writer.WriteLine($"SlotType = {vertexAttribute.SlotType}");
                    writer.WriteLine($"InstanceStepRate = {vertexAttribute.InstanceStepRate}");
                    writer.Indent--;
                }

                writer.WriteLine();
            }

            writer.WriteLine();
            writer.WriteLine("Index buffers:");

            foreach (var indexBuffer in IndexBuffers)
            {
                writer.WriteLine($"Count: {indexBuffer.ElementCount}");
                writer.WriteLine($"Size: {indexBuffer.ElementSizeInBytes}");
                writer.WriteLine();
            }
        }

        private static (int ElementSize, int ElementCount) GetFormatInfo(RenderInputLayoutField attribute)
        {
            return attribute.Format switch
            {
                DXGI_FORMAT.R32G32B32_FLOAT => (4, 3),
                DXGI_FORMAT.R32G32B32A32_FLOAT => (4, 4),
                DXGI_FORMAT.R16G16_UNORM => (2, 2),
                DXGI_FORMAT.R16G16_SNORM => (2, 2),
                DXGI_FORMAT.R16G16_FLOAT => (2, 2),
                DXGI_FORMAT.R32_FLOAT => (4, 1),
                DXGI_FORMAT.R32G32_FLOAT => (4, 2),
                DXGI_FORMAT.R16G16_SINT => (2, 2),
                DXGI_FORMAT.R16G16B16A16_SINT => (2, 4),
                DXGI_FORMAT.R16G16B16A16_FLOAT => (2, 4),
                DXGI_FORMAT.R8G8B8A8_UINT => (1, 4),
                DXGI_FORMAT.R8G8B8A8_UNORM => (1, 4),
                DXGI_FORMAT.R16G16B16A16_UINT => (2, 4),
                DXGI_FORMAT.R16G16B16A16_UNORM => (2, 4),
                _ => throw new NotImplementedException($"Unsupported \"{attribute.SemanticName}\" DXGI_FORMAT.{attribute.Format}"),
            };
        }

        private static IEnumerable<byte> Interleave((IEnumerable<byte> Enumerable, int FormatSize)[] data)
        {
            var enumerators = data.Select(e => (e.Enumerable.GetEnumerator(), e.FormatSize)).ToArray();
            while (true)
            {
                foreach (var (enumerator, formatSize) in enumerators)
                {
                    for (var i = 0; i < formatSize; i++)
                    {
                        if (!enumerator.MoveNext())
                            yield break;
                        yield return enumerator.Current;
                    }
                }
            }
        }

        public static int[] CombineRemapTables(int[][] remapTables)
        {
            remapTables = remapTables.Where(remapTable => remapTable.Length != 0).ToArray();
            var newRemapTable = remapTables[0].AsEnumerable();
            for (var i = 1; i < remapTables.Length; i++)
            {
                var remapTable = remapTables[i];
                newRemapTable = newRemapTable.Select(j => j != -1 ? remapTable[j] : -1);
            }
            return newRemapTable.ToArray();
        }

        public VBIB RemapBoneIndices(int[] remapTable)
        {
            var res = new VBIB();
            res.VertexBuffers.AddRange(VertexBuffers.Select(buf =>
            {
                var blendIndices = Array.FindIndex(buf.InputLayoutFields, field => field.SemanticName == "BLENDINDICES");
                if (blendIndices != -1)
                {
                    var field = buf.InputLayoutFields[blendIndices];
                    var (formatElementSize, formatElementCount) = GetFormatInfo(field);
                    var formatSize = formatElementSize * formatElementCount;
                    buf.Data = buf.Data.ToArray();
                    var bufSpan = buf.Data.AsSpan();
                    for (var i = (int)field.Offset; i < buf.Data.Length; i += (int)buf.ElementSizeInBytes)
                    {
                        for (var j = 0; j < formatSize; j += formatElementSize)
                        {
                            switch (formatElementSize)
                            {
                                case 4:
                                    BitConverter.TryWriteBytes(bufSpan.Slice(i + j),
                                        remapTable[BitConverter.ToUInt32(buf.Data, i + j)]);
                                    break;
                                case 2:
                                    BitConverter.TryWriteBytes(bufSpan.Slice(i + j),
                                        (short)remapTable[BitConverter.ToUInt16(buf.Data, i + j)]);
                                    break;
                                case 1:
                                    buf.Data[i + j] = (byte)remapTable[buf.Data[i + j]];
                                    break;
                                default:
                                    throw new NotImplementedException();
                            }
                        }
                    }
                }
                return buf;
            }));
            res.IndexBuffers.AddRange(IndexBuffers);
            return res;
        }

        private static IEnumerable<T> ChangeBufferStride<T>(T[] oldBuffer, int oldStride, int newStride)
        {
            return Enumerable.Range(0, (oldBuffer.Length / oldStride) * newStride)
                .Select(i =>
                {
                    var index = i % newStride;
                    if (index >= oldStride)
                    {
                        return default;
                    }
                    return oldBuffer[(i / newStride) * oldStride + index];
                });
        }

        private static void FixupVertexBuffer(ref OnDiskBufferData buf)
        {
            var elementCount = (int)buf.ElementCount;
            var elementSize = (int)buf.ElementSizeInBytes;
            var bufData = buf.Data;
            // split fields into separate streams for easier manipulation
            var fields = buf.InputLayoutFields.Select(field =>
            {
                var (formatElementSize, formatElementCount) = GetFormatInfo(field);
                var formatSize = formatElementSize * formatElementCount;
                var data = Enumerable.Range(0, elementCount).SelectMany(i =>
                {
                    var start = i * elementSize + (int)field.Offset;
                    return bufData[start..(start + formatSize)];
                });
                return (field, data, formatSize);
            }).ToArray();

            var blendIndices = Array.FindIndex(fields, field => field.field.SemanticName == "BLENDINDICES");
            var blendWeight = Array.FindIndex(fields, field => field.field.SemanticName == "BLENDWEIGHT"
                || field.field.SemanticName == "BLENDWEIGHTS");

            // add weights if not present
            if (blendIndices != -1)
            {
                var (indicesElementSize, indicesElementCount) = GetFormatInfo(fields[blendIndices].field);
                if (blendWeight == -1)
                {
                    var field = new RenderInputLayoutField()
                    {
                        SemanticName = "BLENDWEIGHT",
                        SlotType = RenderSlotType.RENDER_SLOT_PER_VERTEX,
                        Format = DXGI_FORMAT.R16G16B16A16_FLOAT,
                    };
                    var singleWeight = (Half)(1f / indicesElementCount);
                    var newWeights = Enumerable.Range(0, 4)
                        .SelectMany(i => BitConverter.GetBytes(i < indicesElementCount ? singleWeight : (Half)0f))
                        .ToArray();
                    var data = Enumerable.Range(0, elementCount).SelectMany(i => newWeights);
                    fields = fields.Append((field, data, newWeights.Length)).ToArray();
                    blendWeight = fields.Length - 1;
                }

                if (indicesElementCount != 4)
                {
                    fields[blendIndices].field.Format = fields[blendIndices].field.Format switch
                    {
                        DXGI_FORMAT.R16G16_SINT => DXGI_FORMAT.R16G16B16A16_SINT,
                        _ => throw new NotImplementedException($"Unsupported \"BLENDINDICES\" DXGI_FORMAT.{fields[blendIndices].field.Format}"),
                    };
                    fields[blendIndices].formatSize = 4 * indicesElementSize;
                    fields[blendIndices].data = ChangeBufferStride(fields[blendIndices].data.ToArray(),
                        indicesElementSize * indicesElementCount, fields[blendIndices].formatSize);
                }

                var (weightsElementSize, weightsElementCount) = GetFormatInfo(fields[blendWeight].field);
                if (weightsElementCount != 4)
                {
                    fields[blendWeight].field.Format = fields[blendWeight].field.Format switch
                    {
                        DXGI_FORMAT.R16G16_SINT => DXGI_FORMAT.R16G16B16A16_SINT,
                        _ => throw new NotImplementedException($"Unsupported \"BLENDWEIGHT\" DXGI_FORMAT.{fields[blendWeight].field.Format}"),
                    };
                    fields[blendWeight].formatSize = 4 * weightsElementSize;
                    fields[blendWeight].data = ChangeBufferStride(fields[blendWeight].data.ToArray(),
                        weightsElementSize * weightsElementCount, fields[blendWeight].formatSize);
                }
            }

            // sort fields just in case, and for better alignment
            fields = fields.OrderByDescending(field => field.formatSize).ToArray();

            // establish our own format
            uint currentOffset = 0;
            for (var i = 0; i < fields.Length; i++)
            {
                fields[i].field.SemanticIndex = i;
                fields[i].field.Offset = currentOffset;
                currentOffset += (uint)fields[i].formatSize;
            }

            buf.ElementSizeInBytes = (uint)fields.Aggregate(0, (prev, field) => prev + field.formatSize);
            buf.InputLayoutFields = fields.Select(field => field.field).ToArray();

            // write fixed fields
            buf.Data = Interleave(fields.Select(field => (field.data, field.formatSize)).ToArray()).ToArray();
        }
    }
}
