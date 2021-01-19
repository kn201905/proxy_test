using System;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Text;

namespace proxy
{

class CltContext
{
	// 実験時に、何度も同じ接続をしないようにするためのフラグ
	static bool DBG_ms_connect_once = false;

	// 必ず ReqHead_Reader.EN_bytes_ReqHead_Buf 以上の値にすること
	const int EN_bytes_Context_Buf = 16 * 1024;  // 16 kbytes
	byte[] m_ary_buf_context = new byte[EN_bytes_Context_Buf];

	// --------------------------------------------------
	TcpClient m_clt_tcp_client;
	uint m_idx_clt_context;

	TcpClient m_tgt_tcp_client = null;
	NetworkStream m_ns_tgt = null;

	ReqHead_Reader m_ReqHead_reader;

	// ------------------------------------------------------------------------------------
	public CltContext(TcpClient tcp_client, uint idx_clt_context)
	{
		m_clt_tcp_client = tcp_client;
		m_idx_clt_context = idx_clt_context;

		m_ReqHead_reader = new ReqHead_Reader(m_ary_buf_context);
	}

	// ------------------------------------------------------------------------------------
	public async Task Spawn_Context()
	{
		using (NetworkStream ns_clt = m_clt_tcp_client.GetStream())
		{
         try
         {
				// Clt 側からのリクエストの読み取り実行
			   while (true)
			   {
					if (DBG_ms_connect_once == true) { break; }


					// --------------------------------------------------
					// クライアントからのリクエスト受信
				   KLog.Write($"--- ns_clt.ReadAsync() -> リクエスト受信待機 / idx_clt_context -> {m_idx_clt_context}\r\n");
				   
               // ----- await -----
               // 注意：ReadAsync() はキャンセルトークンに反応しない
               int bytes_recv = await ns_clt.ReadAsync(
							m_ary_buf_context, 0, ReqHead_Reader.EN_bytes_ReqHead_Buf, CancellationToken.None);

				   KLog.Wrt_BkBlue($"\r\n+++ ns.ReadAsync() -> データ受信検知 : {bytes_recv} bytes "
						   + $"/ idx_clt_context -> {m_idx_clt_context}\r\n");
				   if (bytes_recv == 0 || Server.Is_in_ShuttingDown()) { break; }

				   string str_recv = Encoding.UTF8.GetString(m_ary_buf_context, 0, bytes_recv);
				   KLog.Write($"=== Received ===\r\n{str_recv}\r\n=== END ===\r\n\r\n");

					var (ary_buf_tgt_req_head, bytes_tgt_req_head, host_name) = m_ReqHead_reader.Consume(bytes_recv);
					if (bytes_tgt_req_head < 0)
					{
						await ns_clt.WriteAsync(ary_buf_tgt_req_head, 0, ary_buf_tgt_req_head.Length, CancellationToken.None);
						break;
					}


					DBG_ms_connect_once = true;

					// --------------------------------------------------
					// ターゲットへリクエスト送信
					if (m_tgt_tcp_client == null)
					{
						m_tgt_tcp_client = new TcpClient();
						KLog_Study.Write("\r\n--- ターゲットに ConnectAsync() を開始\r\n");
						await m_tgt_tcp_client.ConnectAsync(host_name, 80);  // http ポートに接続
						KLog_Study.Wrt_BkBlue("--- ターゲットに接続完了\r\n");

						m_ns_tgt = m_tgt_tcp_client.GetStream();
					}

					KLog_Study.Write("--- ターゲットにヘッダ送信開始\r\n");
					await m_ns_tgt.WriteAsync(ary_buf_tgt_req_head, 0, bytes_tgt_req_head, CancellationToken.None);
					KLog_Study.Wrt_BkBlue("--- ターゲットにヘッダ送信完了\r\n");

					// --------------------------------------------------
					// ターゲットからレスポンス受信
					KLog_Study.Write("--- ターゲットからのレスポンス待機\r\n");
					bytes_recv = await m_ns_tgt.ReadAsync(m_ary_buf_context, 0, EN_bytes_Context_Buf, CancellationToken.None);
					KLog_Study.Wrt_BkBlue("--- ターゲットからレスポンス受信\r\n");


					using (var file_log = new File_Log())
					{
						file_log.Wrt_Block(m_ary_buf_context, 0, bytes_recv);
					}

					unsafe
					{
						fixed (byte* pary_buf_context = m_ary_buf_context)
						{
							byte* pres = pary_buf_context;

							string str_res_1Line;
							while ((str_res_1Line = DBG_Get_1Line(ref pres)) != null)
							{
								KLog_Study.Write(str_res_1Line);
							}
						}
					}


					break;

			   } // while
         }
         catch (Exception ex)
         {
				if (Server.Is_in_ShuttingDown() == true)
				{
					// Abort_TcpContext() で、アボートされた可能性が高い
					KLog.Write($"+++ 例外補足 : 恐らくサーバーシャットダウンのため。/ idx_clt_context -> {m_idx_clt_context}\r\n");
				}
				else
				{
					KLog.Wrt_BkRed($"\r\n!!! 例外補足 : {ex.ToString()}\r\n");
				}
         }
			finally
			{
				if (m_clt_tcp_client.Connected == true)
				{
					byte[] ary_buf_res_404 = ReqHead_Reader.Get_ary_buf_res_404();
					await ns_clt.WriteAsync(ary_buf_res_404, 0, ary_buf_res_404.Length, CancellationToken.None);
				}

				m_clt_tcp_client.Close();
				m_clt_tcp_client.Dispose();

				if (m_tgt_tcp_client != null)
				{
					m_tgt_tcp_client.Close();
					m_tgt_tcp_client.Dispose();
				}

				if (m_ns_tgt != null)
				{
					m_ns_tgt.Dispose();
				}
			}
		} // using

		KLog.Write($"--- tcp_client.Close() / idx_clt_context -> {m_idx_clt_context}\r\n");
      Server.Remove_frm_task_list(m_idx_clt_context);
	}

	// ------------------------------------------------------------------------------------
	public void Abort_TcpContext()
	{
		try
		{
			m_clt_tcp_client.Close();
			if (m_tgt_tcp_client != null) { m_tgt_tcp_client.Close(); }
		}
		catch (Exception ex)
		{
			KLog.Write($"\r\n!!! 例外補足 : {ex.ToString()}\r\n"); 
		}
	}

	// ------------------------------------------------------------------------------------
	//「改行を含めて」文字列を取り出す
	// 空行の場合は、null が返される
	const int EN_MAX_len_1Line = 300;  // ptr が、contents 部分を指していても大丈夫なようにするための措置
	unsafe string DBG_Get_1Line(ref byte* ptr)
	{
		byte* ptop = ptr;
		int cnt = EN_MAX_len_1Line;

		while (true)
		{
			byte chr = *ptr;
			if (chr == '\r' || chr == '\n')
			{
				if (ptr == ptop) { return null; }

				if (chr == '\r')
				{ ptr += 2; }
				else
				{ ptr++; }
				break;
			}

			if (--cnt == 0)
			{ throw new Exception("!!! ReqHead_Reader.DBG_Get_1Line() : 読み取りオーバー発生"); }

			ptr++;
		}

		// ptr は '\n' の位置を指している
		return Encoding.UTF8.GetString(ptop, (int)(ptr - ptop));
	}

} // Context
} // proxy

