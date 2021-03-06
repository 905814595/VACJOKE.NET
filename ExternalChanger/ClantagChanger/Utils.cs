﻿using System;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.Runtime.InteropServices;

using static ClantagChanger.NTAPI;

namespace ClantagChanger
{
    public static class Utils
    {
        public const int MAX_TEXT_SIZE = 15;

        public static int fnClantagChanged, dwIsInGame;
        public static IntPtr dwClientState;


        public static byte[] ReadMemory(IntPtr hProcess,IntPtr address,int length)
        {
            byte[] data = new byte[length];
            if(!ReadProcessMemory(hProcess,address,data,data.Length,out IntPtr unused))
            {
                return null;
            }
            return data;
        }

        public static T ReadMemory<T>(IntPtr hProcess,IntPtr address)
        {
            var data = ReadMemory(hProcess,address,Marshal.SizeOf(typeof(T)));
            if(data == null)
            {
                return default(T);
            }
            var handle = GCHandle.Alloc(data,GCHandleType.Pinned);
            var result = (T)Marshal.PtrToStructure(handle.AddrOfPinnedObject(),typeof(T));
            handle.Free();
            return result;
        }

        public static string LoadData(Process process)
        {
            if(process == null || process.HasExited)
            {
                return null;
            }
            IntPtr hProcess = process.Handle;
            ProcessModule engineDll = null;
            foreach(ProcessModule module in process.Modules)
            {
                switch(Path.GetFileName(module.FileName))
                {
                case "engine.dll":
                    engineDll = module;
                    continue;
                }
            }
            if(engineDll == null)
            {
                return "Failed to get base address of engine.dll";
            }
            var scanner = new SigScan(hProcess);
            if(!scanner.SelectModule(engineDll.BaseAddress,engineDll.ModuleMemorySize))
            {
                return "Failed to select engine.dll";
            }
            // Find fnClantagChanged
            int scan = (int)scanner.FindPattern("53 56 57 8B DA 8B F9 FF 15",out long time);
            if(scan == 0)
            {
                return "Can't find fnClantagChanged";
            }
            fnClantagChanged = scan;
            // Find dwClientState
            scan = (int)scanner.FindPattern("A1 ? ? ? ? 33 D2 6A 00 6A 00 33 C9 89 B0",out time);
            if(scan == 0)
            {
                return "Can't find dwClientState";
            }
            dwClientState = ReadMemory<IntPtr>(hProcess,new IntPtr(scan + 1));
            // Find dwIsInGame
            scan = (int)scanner.FindPattern("83 B8 ? ? ? ? ? 0F 94 C0 C3",out time);
            if(scan == 0)
            {
                return "Can't find dwClientState";
            }
            dwIsInGame = ReadMemory<int>(hProcess,new IntPtr(scan + 2));
            return null;
        }

        public static int ReadGameStatus(Process process)
        {
            IntPtr hProcess = process.Handle;
            int CClientState = ReadMemory<int>(hProcess,dwClientState);
            return ReadMemory<int>(hProcess,new IntPtr(CClientState + dwIsInGame));
        }

        public static string SetClantag(Process process,string tag,string name)
        {
            if(process == null || process.HasExited)
            {
                return null;
            }
            IntPtr hProcess = process.Handle;

            byte[] shellCode = new byte[]
            {
                0x50, // push eax
                0x51, // push ecx
                0x52, // push edx
                0xB8,0x00,0x00,0x00,0x00, // mov eax, fnClantagChanged
                0xB9,0x00,0x00,0x00,0x00, // mov ecx, tagAddress
                0xBA,0x00,0x00,0x00,0x00, // mov edx, nameAddress
                0xFF,0xD0, // call eax
                0x58, // pop eax
                0x59, // pop ecx
                0x5A, // pop edx
                0xC3, // ret
                0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // tag+name,max 32 bytes
                0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00
            };

            byte[] tagData = Encoding.UTF8.GetBytes(tag),
                nameData = Encoding.UTF8.GetBytes(name);

            int tagSize = Math.Min(tagData.Length,MAX_TEXT_SIZE),
                nameSize = Math.Min(nameData.Length,MAX_TEXT_SIZE),
                codeSize = shellCode.Length - 32,
                allocateSize = codeSize + tagSize + nameSize + 2;

            IntPtr codeAddress = VirtualAllocEx(hProcess,IntPtr.Zero,(uint)allocateSize,AllocationType.Commit | AllocationType.Reserve,MemoryProtection.ExecuteReadWrite);

            int tagAddress = (int)codeAddress + codeSize;
            int nameAddress = tagAddress + tagSize + 1;

            Buffer.BlockCopy(BitConverter.GetBytes(fnClantagChanged),0,shellCode,3 + 1,4);
            Buffer.BlockCopy(BitConverter.GetBytes(tagAddress),0,shellCode,3 + 5 + 1,4);
            Buffer.BlockCopy(BitConverter.GetBytes(nameAddress),0,shellCode,3 + 5 + 5 + 1,4);

            Buffer.BlockCopy(tagData,0,shellCode,codeSize,tagSize);
            Buffer.BlockCopy(nameData,0,shellCode,codeSize + tagSize + 1,nameSize);

            WriteProcessMemory(hProcess,codeAddress,shellCode,allocateSize,out int a);

            IntPtr hThread = CreateRemoteThread(hProcess,IntPtr.Zero,0,codeAddress,IntPtr.Zero,0,out IntPtr b);
            WaitForSingleObject(hThread,INFINITE);
            VirtualFreeEx(hProcess,codeAddress,0,AllocationType.Release);
            return null;
        }
    }
}
