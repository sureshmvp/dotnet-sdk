﻿// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
// ------------------------------------------------------------

namespace Dapr.Actors.Communication
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Runtime.Serialization;
    using System.Text;
    using System.Xml;
    using Dapr.Actors;
    using Dapr.Actors.Resources;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Fault type used by Service Remoting to transfer the exception details from the service to the client.
    /// </summary>
    [DataContract(Name = "RemoteException", Namespace = Constants.Namespace)]
    internal class RemoteException
    {
        private static readonly DataContractSerializer ActorCommunicationExceptionDataSerializer = new DataContractSerializer(typeof(ActorCommunicationExceptionData));


        public RemoteException(List<ArraySegment<byte>> buffers)
        {
            this.Data = buffers;
        }

        /// <summary>
        /// Gets serialized exception or the exception message encoded as UTF8 (if the exception cannot be serialized).
        /// </summary>
        /// <value>Serialized exception or exception message.</value>
        [DataMember(Name = "Data", Order = 0)]
        public List<ArraySegment<byte>> Data { get; private set; }

        /// <summary>
        /// Factory method that constructs the RemoteException from an exception.
        /// </summary>
        /// <param name="exception">Exception.</param>
        /// <returns>Serialized bytes.</returns>
        public static (byte[], string) FromException(Exception exception)
        {
            try
            {
                return (SerializeActorCommunicationException(exception), String.Empty);
            }
            catch (Exception e)
            {
                // failed to serialize the exception, include the information about the exception in the data
                // Add trace diagnostics
                var errorMessage = $"RemoteException, Serialization failed for Exception Type {exception.GetType().FullName} : Reason  {e}";
                return (FromExceptionString(exception), errorMessage);
            }
        }

        /// <summary>
        /// Gets the exception from the RemoteException.
        /// </summary>
        /// <param name="bufferedStream">The stream that contains the serialized exception or exception message.</param>
        /// <param name="result">Exception from the remote side.</param>
        /// <returns>true if there was a valid exception, false otherwise.</returns>
        public static bool ToException(Stream bufferedStream, out Exception result)
        {
            // try to de-serialize the bytes in to exception requestMessage and create service exception
            if (TryDeserializeActorCommunicationException(bufferedStream, out result))
            {
                return true;
            }
           
            bufferedStream.Dispose();

            return false;
        }

        internal static bool TryDeserializeExceptionData(Stream data, out ActorCommunicationExceptionData result, ILogger logger = null)
        {
            try
            {
                var exceptionData = (ActorCommunicationExceptionData)DeserializeActorCommunicationExceptionData(data);
                result = exceptionData;
                return true;
            }
            catch (Exception e)
            {
                // swallowing the exception
                logger?.LogWarning(
                    "RemoteException",
                    " ActorCommunicationExceptionData DeSerialization failed : Reason  {0}",
                    e);
            }

            result = null;
            return false;
        }

        internal static byte[] FromExceptionString(Exception exception)
        {
            var exceptionStringBuilder = new StringBuilder();

            exceptionStringBuilder.AppendFormat(
                CultureInfo.CurrentCulture,
                SR.ErrorExceptionSerializationFailed1,
                exception.GetType().FullName);

            exceptionStringBuilder.AppendLine();

            exceptionStringBuilder.AppendFormat(
                CultureInfo.CurrentCulture,
                SR.ErrorExceptionSerializationFailed2,
                exception);
            return SerializeActorCommunicationException(exception);
        }

        internal static byte[] SerializeActorCommunicationException(Exception exception)
        {
            var exceptionData = new ActorCommunicationExceptionData(exception.GetType().FullName, exception.Message);

            var exceptionBytes = SerializeActorCommunicationExceptionData(exceptionData);

            return exceptionBytes;
        }

        
        private static bool TryDeserializeActorCommunicationException(Stream data, out Exception result, ILogger logger = null)
        {
            try
            {
                data.Seek(0, SeekOrigin.Begin);
                if (TryDeserializeExceptionData(data, out var eData))
                {
                    result = new ActorCommunicationException(eData.Type, eData.Message);
                    return true;
                }
            }
            catch (Exception e)
            {
                // swallowing the exception
                logger?.LogWarning("RemoteException", "DeSerialization failed : Reason  {0}", e);
            }

            result = null;
            return false;
        }

        private static object DeserializeActorCommunicationExceptionData(Stream buffer)
        {
            if ((buffer == null) || (buffer.Length == 0))
            {
                return null;
            }

            using var reader = XmlDictionaryReader.CreateBinaryReader(buffer, XmlDictionaryReaderQuotas.Max);
            return ActorCommunicationExceptionDataSerializer.ReadObject(reader);
        }

        private static byte[] SerializeActorCommunicationExceptionData(ActorCommunicationExceptionData msg)
        {
            if (msg == null)
            {
                return null;
            }

            using var stream = new MemoryStream();
            using var writer = XmlDictionaryWriter.CreateBinaryWriter(stream);
            ActorCommunicationExceptionDataSerializer.WriteObject(writer, msg);
            writer.Flush();
            return stream.ToArray();
        }
    }
}
