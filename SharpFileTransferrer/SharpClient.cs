﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace NahroTo.SharpFileTransferrer
{
	public class SharpClient
	{
		private const int MAX_PACKET_SIZE = 32768;

		private class Address : IDisposable
		{
			public string ip;
			public int port;
			public IEnumerable<string> files;
			public CancellationTokenSource cts;

			public Address(string ip, int port, IEnumerable<string> files)
			{
				this.ip = ip;
				this.port = port;
				this.files = files;
				cts = new CancellationTokenSource();
			}

			public void Dispose()
			{
				Dispose(true);
				GC.SuppressFinalize(this);
			}

			protected virtual void Dispose(bool disposing)
			{
				if (disposing && (cts != null))
				{
					cts.Cancel();
					cts.Dispose();
					cts = null;
				}
			}
		}

		public static void SendFile(string ipadress, int port, string filePath)
		{
			ThreadPool.QueueUserWorkItem(token =>
			{
				ConnectAsClient(new Address(ipadress, port, new string[] { filePath }));
			});
			Thread.Sleep(500);
		}

		public static void SendFiles(string ipadress, int port, IEnumerable<string> filePaths)
		{
			ThreadPool.QueueUserWorkItem(token =>
			{
				ConnectAsClient(new Address(ipadress, port, filePaths));
			});
			Thread.Sleep(500);
		}


		private static void ConnectAsClient(Address param) // #3
		{
			using (TcpClient client = new TcpClient())
			{
				client.Connect(IPAddress.Parse(param.ip), param.port);
				using (NetworkStream stream = client.GetStream())
				{
					bool newFile = true;
					stream.Write(BitConverter.GetBytes(param.files.Count()), 0, 4);
					for (int i = 0; i < param.files.Count(); i++)
					{
						string filePath = param.files.ElementAt(i);
						int fileLength = File.ReadAllBytes(filePath).Length;
						double amountFilePackets = 1; // one data info packet
						if (fileLength > MAX_PACKET_SIZE)
						{
							amountFilePackets = Math.Ceiling((double)fileLength / MAX_PACKET_SIZE);
						}
						if (newFile)
						{
							// WRITE PACKET 0 (FILE INFO)
							byte[] dataFilename = Encoding.ASCII.GetBytes(System.IO.Path.GetFileName(filePath));
							int packetLength0 = dataFilename.Length;
							// write packetlength0 (4 bytes)
							stream.Write(BitConverter.GetBytes(packetLength0), 0, 4);
							byte[] header0 = { 0 };
							// write header0 (1 byte)
							stream.Write(header0, 0, 1);
							// write dataFilename (packetlength0 bytes)
							stream.Write(dataFilename, 0, packetLength0);

							newFile = false;
						}
						// WRITE PACKET 1 (FILE DATA)
						int packetsLeftToWrite = (int)amountFilePackets;
						int packetsWritten = 0;
						int remainingBytes;
						while (packetsLeftToWrite != 0)
						{
							byte[] dataFilePacket;
							remainingBytes = File.ReadAllBytes(filePath).Skip(packetsWritten * MAX_PACKET_SIZE).ToArray().Length;
							if (!(remainingBytes <= MAX_PACKET_SIZE)) // if remainig bytes are NOT enough for 1 packet
							{
								dataFilePacket = File.ReadAllBytes(filePath).Skip(packetsWritten * MAX_PACKET_SIZE).Take(MAX_PACKET_SIZE).ToArray();
							}
							else // if last packet
							{
								dataFilePacket = File.ReadAllBytes(filePath).Skip(packetsWritten * MAX_PACKET_SIZE).Take(File.ReadAllBytes(filePath).Length - packetsWritten * MAX_PACKET_SIZE).ToArray();
								newFile = true;
							}
							int packetLength1 = dataFilePacket.Length;
							// write packetlength1 (4 bytes)
							stream.Write(BitConverter.GetBytes(packetLength1), 0, 4);
							packetsLeftToWrite--;
							byte[] header = new byte[1];
							if (packetsLeftToWrite == 0)
							{
								header[0] = 2;
							}
							else
							{
								header[0] = 1;
							}
							// write header1 (1 byte)
							stream.Write(header, 0, 1);

							// write dataFilePacket
							stream.Write(dataFilePacket, 0, packetLength1);
							packetsWritten++;
						}
					}
				}
			}
			param.Dispose();
		}
	}
}
