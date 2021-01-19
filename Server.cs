using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Diagnostics;

namespace proxy
{

static class Server
{
	// TcpListener 用のポート番号
	const int EN_num_tcp_linster_port = 13000;
	static TcpListener m_tcp_listener_for_Abort = null;

	// -------------------------------------------------
	// NetworkStream は、キャンセルトークンに反応しない
//	public static CancellationTokenSource ms_cts_shutdown;
	static bool msb_in_shutting_down = false;
	public static bool Is_in_ShuttingDown() => msb_in_shutting_down;

	class Task_TcpContext
	{
		public Task m_task = null;
		public CltContext m_clt_context = null;
	}

	static SortedList<uint, Task_TcpContext> m_task_list = new SortedList<uint, Task_TcpContext>();

	// ------------------------------------------------------------------------------------
	public static async void Start()
	{
		TcpListener tcp_listener = null;

		KLog.Write("--- Start Server\r\n");
		uint idx_clt_context = 0;

		try
		{
			// ローカルアドレスを指定するための処置。リリース版では不要
			IPAddress localAddr = IPAddress.Parse("127.0.0.1");
			tcp_listener = new TcpListener(localAddr, EN_num_tcp_linster_port);
			m_tcp_listener_for_Abort = tcp_listener;
			tcp_listener.Start();  // TcpListener の開始

			// NetworkStream は、キャンセルトークンに反応しない
//			using (ms_cts_shutdown = new CancellationTokenSource())
			{
				while(true)
				{
					KLog.Write("--- tcp_listener.AcceptTcpClientAsync() -> 接続待機\r\n");
					// AcceptTcpClientAsync() はキャンセルトークンをサポートしていない
					TcpClient tcp_client = await tcp_listener.AcceptTcpClientAsync();
					idx_clt_context++;
					// 本当はここでサーバーシャットダウンを補足したいが、現時点では例外で while ループを抜けている
					if (msb_in_shutting_down) { break; }

					KLog.Wrt_BkBlue("\r\n+++ tcp_listener.AcceptTcpClientAsync() -> 新規接続検知\r\n");

					Socket tcp_socket = tcp_client.Client;
					IPEndPoint tcp_endpoint = (IPEndPoint)tcp_socket.RemoteEndPoint;
					KLog.Wrt_BkBlue($"--- Connected : idx_clt_context -> {idx_clt_context} / port -> {tcp_endpoint.Port}\r\n");

					var clt_context = new CltContext(tcp_client, idx_clt_context);
					Task ret_task = clt_context.Spawn_Context();

					m_task_list.Add(idx_clt_context
						, new Task_TcpContext{ m_task = ret_task, m_clt_context = clt_context });
				} // while
			} // using
		} //  try
		catch (SocketException ex)
		{
			KLog.Write($"!!! SocketException : {ex.ToString()}\r\n");
		}
		catch (Exception ex)
		{
			if (msb_in_shutting_down == true)
			{
				// StopServer() で、listener_socket.Close() とするためであると考えられる
				KLog.Write($"+++ 例外補足 : 恐らくサーバーシャットダウンのため。\r\n");
			}
			else
			{
				KLog.Write($"\r\n!!! 例外補足 : {ex.ToString()}\r\n");
			}
		}
		finally
		{
			tcp_listener.Stop();
		}

		KLog.Write("\r\n+++ Terminated Server\r\n");
	}

	// ------------------------------------------------------------------------------------
	public static void Remove_frm_task_list(uint idx_clt_context)
	{
		// シャットダウン中の foreach(var kvp in m_task_list) において、m_task_list の変更はできないため
		if (msb_in_shutting_down == true) { return; }

		m_task_list.Remove(idx_clt_context);
		KLog.Write($"--- Remove_frm_task_list() : idx_clt_context -> {idx_clt_context}"
				+ $" / m_task_list の個数 -> {m_task_list.Count}\r\n");
	}

	// ------------------------------------------------------------------------------------
	public static async Task StopServer()
	{
		KLog.Write("--- シャットダウンシグナルを送信します。\r\n");
		msb_in_shutting_down = true;
		// NetworkStream は、キャンセルトークンに反応しない
//		ms_cts_shutdown.Cancel();

		try
		{
			Socket listener_socket = m_tcp_listener_for_Abort.Server;
			listener_socket.Close();
		}
		catch (Exception ex)
		{
			KLog.Write($"\r\n!!! 例外補足 : {ex.ToString()}\r\n");
		}

		KLog.Write($"\r\n+++ 強制停止させる TcpContext の個数 -> {m_task_list.Count}\r\n");
		foreach (var kvp in m_task_list)
		{
			uint idx_clt_context = kvp.Key;
			Task task_clt_context = kvp.Value.m_task;
			CltContext clt_context = kvp.Value.m_clt_context;

			KLog.Write($"--- Abort : idx_clt_context -> {idx_clt_context}\r\n");
			clt_context.Abort_TcpContext();

			await task_clt_context;
		}
	}

} // class Server
} // namespace proxy
