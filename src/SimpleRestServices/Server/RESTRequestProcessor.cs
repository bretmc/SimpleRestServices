﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Net;
using System.ServiceModel.Web;
using System.Text;
using System.Web;
using JSIStudios.SimpleRESTServices.Core;
using JSIStudios.SimpleRESTServices.Core.Exceptions;
using JSIStudios.SimpleRESTServices.Server.EventArgs;

namespace JSIStudios.SimpleRESTServices.Server
{
    public class RESTRequestProcessor : IRequestProcessor
    {
        public event EventHandler<RESTRequestStartedEventArgs> RequestStarted;
        public event EventHandler<RESTRequestCompletedEventArgs> RequestCompleted;

        public event EventHandler<RESTRequestErrorEventArgs> OnError;

        #region Interface methods
        
        public virtual void Execute(Action<Guid> callBack)
        {
            Execute(callBack, null);
        }

        public virtual void Execute(Action<Guid> callBack, NameValueCollection responseHeaders)
        {
            ExecuteSafely<object>((requestId) =>
            {
                callBack(requestId);
                return null;
            }, responseHeaders);
        }

        public virtual TResult Execute<TResult>(Func<Guid, TResult> callBack)
        {
            return Execute(callBack, null);
        }

        public virtual TResult Execute<TResult>(Func<Guid, TResult> callBack, NameValueCollection responseHeaders)
        {
            TResult result = default(TResult);

            ExecuteSafely((requestId) =>
            {
                result = callBack(requestId);
                return result;
            }, responseHeaders);

            if (WebOperationContext.Current != null && WebOperationContext.Current.OutgoingResponse.StatusCode == HttpStatusCode.OK)
                if (result == null)
                    WebOperationContext.Current.OutgoingResponse.StatusCode = HttpStatusCode.NotFound;
            
            return result;
        }

        #endregion

        #region Private methods

        protected void ExecuteSafely<TResult>(Func<Guid, TResult> callBack, NameValueCollection responseHeaders)
        {
            var requestId = Guid.NewGuid();

            try
            {
                if (RequestStarted != null)
                    RequestStarted(this, new RESTRequestStartedEventArgs(requestId, GetHttpRequest(HttpContext.Current.Request)));

                var stopwatch = new Stopwatch();
                stopwatch.Start();

                var result = callBack(requestId);

                stopwatch.Stop();

                if (RequestCompleted != null)
                    RequestCompleted(this, new RESTRequestCompletedEventArgs(requestId, result, stopwatch.ElapsedMilliseconds));

                if (EqualityComparer<TResult>.Default.Equals(result, default(TResult)))
                    throw new HttpResourceNotFoundException("Resource not found.");

                if (WebOperationContext.Current != null)
                {
                    WebOperationContext.Current.OutgoingResponse.StatusCode = HttpStatusCode.OK;
                    if (responseHeaders != null)
                        WebOperationContext.Current.OutgoingResponse.Headers.Add(responseHeaders);
                }
            }
            catch (BadWebRequestException ex)
            {
                if (OnError != null)
                    OnError(this, new RESTRequestErrorEventArgs(requestId, ex));

                SetHttpErrorStatusCode(ex.Message, HttpStatusCode.BadRequest);
            }
            catch (HttpResourceNotFoundException ex)
            {
                if (OnError != null)
                    OnError(this, new RESTRequestErrorEventArgs(requestId, ex));

                SetHttpErrorStatusCode(ex.Message, HttpStatusCode.NotFound);
            }
            catch (HttpResourceNotModifiedException ex)
            {
                if (OnError != null)
                    OnError(this, new RESTRequestErrorEventArgs(requestId, ex));

                if (WebOperationContext.Current != null)
                    WebOperationContext.Current.OutgoingResponse.StatusCode = HttpStatusCode.NotModified;
                else
                    SetHttpErrorStatusCode(HttpStatusCode.NotModified);
            }
            catch (WebFaultException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (OnError != null)
                    OnError(this, new RESTRequestErrorEventArgs(requestId, ex));

                if (WebOperationContext.Current != null)
                    WebOperationContext.Current.OutgoingResponse.StatusCode = HttpStatusCode.InternalServerError;

                SetHttpErrorStatusCode(string.Format("There was an error processing the request:{0}", ex.Message), HttpStatusCode.InternalServerError);
            }
        }

        private static string GetHttpRequest(HttpRequest request)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Format("{0} {1}", request.RequestType, request.RawUrl));
            sb.AppendLine(request.ServerVariables["ALL_RAW"]);

            return sb.ToString();
        }

        protected virtual void SetHttpErrorStatusCode<T>(T value, HttpStatusCode statusCode)
        {
            throw new WebFaultException<T>(value, statusCode);
        }

        protected virtual void SetHttpErrorStatusCode(string content, HttpStatusCode statusCode)
        {
            throw new WebFaultException<string>(content, statusCode);
        }

        protected virtual void SetHttpErrorStatusCode(HttpStatusCode statusCode)
        {
            throw new WebFaultException(statusCode);
        }

        #endregion
    }
}
