﻿using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace th_hack_tools
{
    /// <summary>
    /// Loads an entire binary table and reads/writes all subtables.
    /// </summary>
    public class TableController
    {

        public List<ITable> Tables;
        public int total_length = 0;
        long offset;

        public TableController(FileStream stream, long _offset = 0)
        {
            offset = _offset;
            byte[] bits = new byte[4];
            stream.Seek(offset, SeekOrigin.Begin);
            stream.Read(bits, 0, 4);

            Tables = new List<ITable>();
            int initial_size = BitConverter.ToInt32(bits, 0);

            for (int i = 0; i < initial_size; i++)
            {
                byte[] table;
                byte[] table_offset = new byte[4];
                byte[] table_length = new byte[4];

                stream.Seek(offset + 4 + i * 8, SeekOrigin.Begin);
                stream.Read(table_offset, 0, 4);
                stream.Read(table_length, 0, 4);

                table = new byte[BitConverter.ToInt32(table_length, 0)];

                stream.Seek(offset + BitConverter.ToInt32(table_offset, 0), SeekOrigin.Begin);
                stream.Read(table, 0, table.Length);

                Create_Table(table);

                if (i == initial_size - 1)
                    total_length = BitConverter.ToInt32(table_offset, 0) + BitConverter.ToInt32(table_length, 0);

            }

        }

        public TableController(byte[] data)
        {
            offset = 0;
            byte[] bits = data.Take(4).ToArray();
            Tables = new List<ITable>();
            int initial_size = BitConverter.ToInt32(bits, 0);

            for (int i = 0; i < initial_size; i++)
            {
                int skip = 4 + i * 8;
                byte[] table_offset = data.Skip(skip).Take(4).ToArray();
                byte[] table_length = data.Skip(skip + 4).Take(4).ToArray();

                byte[] table = data.Skip(BitConverter.ToInt32(table_offset, 0)).Take(BitConverter.ToInt32(table_length, 0)).ToArray();

                Create_Table(table);

            }
        }

        private void Create_Table(byte[] table)
        {
            if (Enumerable.SequenceEqual(table.Take(4), new byte[] { 0x0, 0x19, 0x12, 0x16 }))
            {
                Tables.Add(new BinaryTable(table));
            }
            else
            {
                Tables.Add(new TextTable(table));
            }
        }

        public List<byte> Write()
        {
            List<byte> new_header = new List<byte>();
            List<byte> new_data = new List<byte>();

            new_header.AddRange(BitConverter.GetBytes(Tables.Count));

            
            int table_offset = 4 + Tables.Count * 8;
            total_length = table_offset;
            foreach (ITable table in Tables)
            {
                List<byte> new_table = table.Save();
                

                new_header.AddRange(BitConverter.GetBytes(table_offset));
                new_header.AddRange(BitConverter.GetBytes(new_table.Count));

                while (new_table.Count % 4 != 0)
                {
                    new_table.Add((byte)0);
                }

                total_length += new_table.Count;
                new_data.AddRange(new_table);
                table_offset += new_table.Count;
            }

            // Add contents to end of header

            new_header.AddRange(new_data);

            return new_header;
        }

    }

    /// <summary>
    /// Interface for handling different kinds of tables
    /// </summary>
    public interface ITable
    {
        byte[] header { get; set; }

        List<byte[]> contents { get; set; }

        int total_length { get; set; }

        List<byte> Save();

    }

    /// <summary>
    /// Reads/Writes binary subtables.
    /// </summary>
    public class BinaryTable : ITable
    {

        public byte[] header { get; set; }
        public List<byte[]> contents { get; set; }
        public int total_length { get; set; }

        public BinaryTable(byte[] data)
        {

            header = new byte[64];
            contents = new List<byte[]>();
            total_length = 64;

            Array.Copy(data, 0, header, 0, header.Length);
            int item_count = BitConverter.ToInt32(header, 4);
            int item_length = BitConverter.ToInt32(header, 8);

            for (int i = 0; i < item_count; i++)
            {
                byte[] item = new byte[item_length];
                int index = header.Length + item_length * i;
                Array.Copy(data, index, item, 0, item.Length);
                contents.Add(item);
            }

            total_length += item_count * item_length;

        }

        public List<byte> Save()
        {
            total_length = 64;
            byte[] item_count_raw = BitConverter.GetBytes(contents.Count);
            Array.Copy(item_count_raw, 0, header, 4, 4);

            List<byte> new_table = new List<byte>();
            new_table.AddRange(header);

            int item_length = BitConverter.ToInt32(header, 8);

            foreach (byte[] item in contents)
            {
                new_table.AddRange(item);
            }

            total_length += contents.Count * item_length;
            return new_table;
        }

    }

    /// <summary>
    /// Reads/Writes subtables containing text archives
    /// </summary>
    public class TextTable: ITable
    {
        public byte[] header { get; set; }
        public List<byte[]> contents { get; set; }
        public int total_length { get; set; }

        public List<Text_Archive> archives = new List<Text_Archive>();

        public TextTable(byte[] data)
        {
            int table_count = BitConverter.ToInt32(data, 0);
            
            List<int> table_offsets = new List<int>();
            List<int> table_lengths = new List<int>();

            contents = new List<byte[]>();

            for (int i = 4; i < table_count * 8; i += 8)
            {
                int archive_offset = BitConverter.ToInt32(data, i);
                int archive_length = BitConverter.ToInt32(data, i + 4);
                byte[] table = new byte[archive_length];

                Array.Copy(data, archive_offset, table, 0, archive_length);
                contents.Add(table);
                table_offsets.Add(archive_offset);
                table_lengths.Add(archive_length);
                archives.Add(new Text_Archive(table));
            }
            int header_size = 4 + table_count * 8;
            header = data.Take(header_size).ToArray();

            total_length = table_offsets.Last() + table_lengths.Last();
        }

        public short get_type(int archive)
        {
            return archives[archive].type;
        }

        public int get_args1(int archive, int string_index)
        {
            if (get_type(archive) == 2)
                return archives[archive].args1[string_index];
            else
                return -1;
        }

        public int get_args2(int archive, int string_index)
        {
            if (get_type(archive) == 2)
                return archives[archive].args2[string_index];
            else
                return -1;
        }

        public void set_arg1(int archive, int arg, int index)
        {
            archives[archive].args1[index] = arg;
        }

        public void set_arg2(int archive, int arg, int index)
        {
            archives[archive].args2[index] = arg;
        }

        public List<string> get_contents(int archive)
        {
            List<string> strings = archives[archive].load_contents(contents[archive]);
            return strings;
        }

        public void set_contents(int archive, List<string> new_contents)
        {
            contents[archive] = archives[archive].Write(new_contents);
        }

        public List<byte> Save()
        {
            total_length = 0;
            for (int i=0; i < archives.Count; i++)
            {
                total_length += contents[i].Length;
            }

            List<byte> new_table = new List<byte>();
            int header_size = 4 + archives.Count * 8;

            int archive_offset = header_size;
            int index = 0;

            for (int i=4;i<header_size;i+=8)
            {
                int length = contents[index].Length;
                byte[] length_raw = BitConverter.GetBytes(length);
                byte[] offset_raw = BitConverter.GetBytes(archive_offset);

                Array.Copy(offset_raw, 0, header, i, 4);
                Array.Copy(length_raw, 0, header, i+4, 4);

                archive_offset += length;

                while (archive_offset % 4 != 0)
                {
                    archive_offset++;
                }

                index++;
            }

            new_table.AddRange(header);
            total_length += header_size;

            foreach (byte[] item in contents)
            {
                List<byte> new_item = new List<byte>(item);

                while (new_item.Count % 4 != 0)
                {
                    new_item.Add((byte)0);
                }

                new_table.AddRange(new_item);
            }

            return new_table;
        }
    }

    /// <summary>
    /// Reads/Writes text archives
    /// </summary>
    public class Text_Archive
    {
        
        byte[] header;
        public List<uint> pointers = new List<uint>();
        public short type = 1;

        public List<int> args1 = new List<int>(); // only if type=2
        public List<int> args2 = new List<int>(); // only if type=2

        public Text_Archive(byte[] bin)
        {
            int header_size = BitConverter.ToInt16(bin, 12);
            int pointer_size = BitConverter.ToInt16(bin, 10);

            if (header_size >= 24)
                type = 2;

            header = bin.Take(header_size).ToArray();

            int pointer_count = BitConverter.ToInt16(header, 8);
            int pointer_end = pointer_count * pointer_size + header.Length;

            for (int i = header.Length; i < pointer_end; i += pointer_size)
            {
                uint pointer = BitConverter.ToUInt32(bin, i);
                pointers.Add(pointer);

                if (type == 2)
                {
                    int character = BitConverter.ToInt32(bin, i+4);
                    args1.Add(character);

                    int voice = BitConverter.ToInt32(bin, i + 8);
                    args2.Add(voice);

                }

            }

        }

        public List<string> load_contents(byte[] bin)
        {
            List<string> strings = new List<string>();
            foreach (uint pointer in pointers)
            {
                uint i = pointer + (uint)header.Length;
                List<byte> current_string = new List<byte>();
                while (i < bin.Length)
                {
                    if (bin[i] != 0)
                        current_string.Add(bin[i]);
                    else
                        break;
                    i++;
                }
                strings.Add(Encoding.UTF8.GetString(current_string.ToArray()));
            }
            return strings;
        }

        public byte[] Write(List<string> contents)
        {
            List<uint> new_pointers = new List<uint>();
            List<byte> new_contents = new List<byte>();
            List<byte> pointers_raw = new List<byte>();

            int pointer_size = BitConverter.ToInt16(header, 10);

            int pos = contents.Count * pointer_size;
            foreach (string s in contents)
            {
                byte[] string_raw = Encoding.UTF8.GetBytes(s);

                new_pointers.Add((uint)pos);
                new_contents.AddRange(string_raw);
                new_contents.Add((byte)0);
                pos += string_raw.Length + 1;
            }

            byte[] new_header = header;

            ushort pointer_count = (ushort)new_pointers.Count;
            int pointer_end = pointer_count * pointer_size + header.Length;

            Array.Copy(BitConverter.GetBytes(pointer_count), 0, new_header, 8, 2);

            foreach (int pointer in new_pointers)
            {
                pointers_raw.AddRange(BitConverter.GetBytes(pointer));
            }

            byte[] newbin = new_header.Concat(pointers_raw).Concat(new_contents).ToArray();

            int total_length = newbin.Length;

            while (total_length > ushort.MaxValue)
            {
                total_length -= ushort.MaxValue + 1;
            }

            Array.Copy(BitConverter.GetBytes((ushort)total_length), 0, newbin, 4, 2);

            if (type == 2)
            {
                int current = 0;
                for (int i = new_header.Length; i < pointer_end; i += pointer_size)
                {
                    Array.Copy(BitConverter.GetBytes(args1[current]), 0, newbin, i + 4, 4);
                    Array.Copy(BitConverter.GetBytes(args2[current]), 0, newbin, i + 8, 4);
                    current++;
                }
            }

            header = newbin.Take(header.Length).ToArray();
            pointers = new_pointers;

            return newbin;
        }

    }

}
