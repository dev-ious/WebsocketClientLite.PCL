﻿using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using IWebsocketClientLite.PCL;
using WebsocketClientLite.PCL.CustomException;
using WebsocketClientLite.PCL.Model;
using static WebsocketClientLite.PCL.Helper.WebsocketMasking;

namespace WebsocketClientLite.PCL.Service
{
    internal class WebsocketSenderHandler
    {
        private bool _isSendingMultipleFrames;

        private readonly IObserver<ConnectionStatus> _observerConnectionStatus;

        internal WebsocketSenderHandler(
            IObserver<ConnectionStatus> observerConnectionStatus)
        {
            _observerConnectionStatus = observerConnectionStatus;
        }

        internal async Task SendTextAsync(Stream tcpStream, string message)
        {
            var msgAsBytes = Encoding.UTF8.GetBytes(message);

            await ComposeFrameAndSendAsync(tcpStream, msgAsBytes, FrameType.Single);
        }

        internal async Task SendTextAsync(Stream tcpStream, string[] messageList)
        {

            if (messageList.Length < 1) return;

            if (messageList.Length == 1)
            {
                await ComposeFrameAndSendAsync(tcpStream, Encoding.UTF8.GetBytes(messageList[0]), FrameType.Single);
            }

            await ComposeFrameAndSendAsync(tcpStream, Encoding.UTF8.GetBytes(messageList[0]), FrameType.FirstOfMultipleFrames);

            for (var i = 1; i < messageList.Length - 1; i++)
            {
                await ComposeFrameAndSendAsync(tcpStream, Encoding.UTF8.GetBytes(messageList[i]), FrameType.Continuation);
            }
            await ComposeFrameAndSendAsync(tcpStream, Encoding.UTF8.GetBytes(messageList.Last()), FrameType.LastInMultipleFrames);
        }

        
        internal async Task SendTextMultiFrameAsync(Stream tcpStream, string message, FrameType frameType)
        {
            if (_isSendingMultipleFrames)
            {
                if (frameType == FrameType.FirstOfMultipleFrames || frameType == FrameType.Single)
                {
                    await ComposeFrameAndSendAsync(tcpStream, Encoding.UTF8.GetBytes("_sequence aborted error_"), FrameType.LastInMultipleFrames);
                    _isSendingMultipleFrames = false;
                    throw new WebsocketClientLiteException("Multiple frames is progress. Frame must be a Continuation Frame or Last Frams in sequence. Multiple frame sequence aborted and finalized");
                }
            }

            if (!_isSendingMultipleFrames && frameType != FrameType.FirstOfMultipleFrames)
            {
                if (frameType == FrameType.Continuation || frameType == FrameType.LastInMultipleFrames)
                {
                    throw new WebsocketClientLiteException("Multiple frames sequence is not in initiated. Frame cannot be of a Continuation Frame or a Last Frame type");
                }
            }

            switch (frameType)
            {
                case FrameType.Single:
                    await ComposeFrameAndSendAsync(tcpStream, Encoding.UTF8.GetBytes(message), FrameType.Single);
                    break;
                case FrameType.FirstOfMultipleFrames:
                    _isSendingMultipleFrames = true;
                    await ComposeFrameAndSendAsync(tcpStream, Encoding.UTF8.GetBytes(message), FrameType.FirstOfMultipleFrames);
                    break;
                case FrameType.LastInMultipleFrames:
                    await ComposeFrameAndSendAsync(tcpStream, Encoding.UTF8.GetBytes(message), FrameType.LastInMultipleFrames);
                    _isSendingMultipleFrames = false;
                    break;
                case FrameType.Continuation:
                    await ComposeFrameAndSendAsync(tcpStream, Encoding.UTF8.GetBytes(message), FrameType.Continuation);
                    break;
            }
        }

        internal async Task SendCloseHandshakeAsync(Stream tcpStream, StatusCodes statusCode)
        {
            var closeFrameBodyCode = BitConverter.GetBytes((ushort)statusCode);
            var reason = Encoding.UTF8.GetBytes(statusCode.ToString());

            await ComposeFrameAndSendAsync(tcpStream, closeFrameBodyCode.Concat(reason).ToArray(),
                FrameType.CloseControlFrame);
        }

        private async Task ComposeFrameAndSendAsync(Stream tcpStream, byte[] content, FrameType frameType)
        {
            var firstByte = new byte[1];

            switch (frameType)
            {
                case FrameType.Single:
                    firstByte[0] = 129;
                    break;
                case FrameType.FirstOfMultipleFrames:
                    firstByte[0] = 1;
                    break;
                case FrameType.Continuation:
                    firstByte[0] = 0;
                    break;
                case FrameType.LastInMultipleFrames:
                    firstByte[0] = 128;
                    break;
                case FrameType.CloseControlFrame:
                    firstByte[0] = 136;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(frameType), frameType, null);
            }

            var payloadBytes = CreatePayloadBytes(content.Length, isMasking: true);
            var maskKey = CreateMaskKey();
            var maskedMessage = Encode(content, maskKey);
            var frame = firstByte.Concat(payloadBytes).Concat(maskKey).Concat(maskedMessage).ToArray();

            await SendFrameAsync(tcpStream, frame);
        }

        private async Task SendFrameAsync(Stream tcpStream, byte[] frame)
        {
            if (!tcpStream.CanWrite)
            {
                throw new WebsocketClientLiteException("Websocket connection have been closed");
            }

            try
            {
                await tcpStream.WriteAsync(frame, 0, frame.Length);
                await tcpStream.FlushAsync();
            }
            catch (Exception ex)
            {
                _observerConnectionStatus.OnError(ex);
            }

        }
    }
}
