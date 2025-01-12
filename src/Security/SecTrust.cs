// 
// SecTrust.cs: Implements the managed SecTrust wrapper.
//
// Authors: 
//  Sebastien Pouliot  <sebastien@xamarin.com>
//
// Copyright 2013-2014 Xamarin Inc.
// Copyright 2019 Microsoft Corporation
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography.X509Certificates;
using ObjCRuntime;
using CoreFoundation;
using Foundation;

namespace Security {

	public delegate void SecTrustCallback (SecTrust trust, SecTrustResult trustResult);
	public delegate void SecTrustWithErrorCallback (SecTrust trust, bool result, NSError /* CFErrorRef _Nullable */ error);

	public partial class SecTrust {

		public SecTrust (SecCertificate certificate, SecPolicy policy)
		{
			if (certificate == null)
				throw new ArgumentNullException ("certificate");

			Initialize (certificate.Handle, policy);
		}

		[iOS (7,0)]
		[DllImport (Constants.SecurityLibrary)]
		extern static SecStatusCode /* OSStatus */ SecTrustCopyPolicies (IntPtr /* SecTrustRef */ trust, ref IntPtr /* CFArrayRef* */ policies);

		[iOS (7,0)]
		public SecPolicy[] GetPolicies ()
		{
			IntPtr p = IntPtr.Zero;
			SecStatusCode result = SecTrustCopyPolicies (handle, ref p);
			if (result != SecStatusCode.Success)
				throw new InvalidOperationException (result.ToString ());
			return NSArray.ArrayFromHandle<SecPolicy> (p);
		}

		[DllImport (Constants.SecurityLibrary)]
		extern static SecStatusCode /* OSStatus */ SecTrustSetPolicies (IntPtr /* SecTrustRef */ trust, IntPtr /* CFTypeRef */ policies);

		// the API accept the handle for a single policy or an array of them
		void SetPolicies (IntPtr policy)
		{
			SecStatusCode result = SecTrustSetPolicies (handle, policy);
			if (result != SecStatusCode.Success)
				throw new InvalidOperationException (result.ToString ());
		}

		public void SetPolicy (SecPolicy policy)
		{
			if (policy == null)
				throw new ArgumentNullException ("policy");

			SetPolicies (policy.Handle);
		}

		public void SetPolicies (IEnumerable<SecPolicy> policies)
		{
			if (policies == null)
				throw new ArgumentNullException ("policies");

			using (var array = NSArray.FromNSObjects (policies.ToArray ()))
				SetPolicies (array.Handle);
		}

		public void SetPolicies (NSArray policies)
		{
			if (policies == null)
				throw new ArgumentNullException ("policies");

			SetPolicies (policies.Handle);
		}

		[iOS (7,0)][Mac (10,9)]
		[DllImport (Constants.SecurityLibrary)]
		extern static SecStatusCode /* OSStatus */ SecTrustGetNetworkFetchAllowed (IntPtr /* SecTrustRef */ trust, [MarshalAs (UnmanagedType.I1)] out bool /* Boolean* */ allowFetch);

		[iOS (7,0)][Mac (10,9)]
		[DllImport (Constants.SecurityLibrary)]
		extern static SecStatusCode /* OSStatus */ SecTrustSetNetworkFetchAllowed (IntPtr /* SecTrustRef */ trust, [MarshalAs (UnmanagedType.I1)] bool /* Boolean */ allowFetch);

		[iOS (7,0)][Mac (10,9)]
		public bool NetworkFetchAllowed {
			get {
				bool value;
				SecStatusCode result = SecTrustGetNetworkFetchAllowed (handle, out value);
				if (result != SecStatusCode.Success)
					throw new InvalidOperationException (result.ToString ());
				return value;
			}
			set {
				SecStatusCode result = SecTrustSetNetworkFetchAllowed (handle, value);
				if (result != SecStatusCode.Success)
					throw new InvalidOperationException (result.ToString ());
			}
		}

		[iOS (7,0)]
		[DllImport (Constants.SecurityLibrary)]
		extern static SecStatusCode /* OSStatus */ SecTrustCopyCustomAnchorCertificates (IntPtr /* SecTrustRef */ trust, out IntPtr /* CFArrayRef* */ anchors);

		[iOS (7,0)]
		public SecCertificate[] GetCustomAnchorCertificates  ()
		{
			IntPtr p;
			SecStatusCode result = SecTrustCopyCustomAnchorCertificates (handle, out p);
			if (result != SecStatusCode.Success)
				throw new InvalidOperationException (result.ToString ());
			return NSArray.ArrayFromHandle<SecCertificate> (p);
		}

		[iOS (7,0)]
		[Deprecated (PlatformName.MacOSX, 10,15, message: "Use 'Evaluate (DispatchQueue, SecTrustWithErrorCallback)' instead.")]
		[Deprecated (PlatformName.iOS, 13,0, message: "Use 'Evaluate (DispatchQueue, SecTrustWithErrorCallback)' instead.")]
		[Deprecated (PlatformName.WatchOS, 6,0, message: "Use 'Evaluate (DispatchQueue, SecTrustWithErrorCallback)' instead.")]
		[Deprecated (PlatformName.TvOS, 13,0, message: "Use 'Evaluate (DispatchQueue, SecTrustWithErrorCallback)' instead.")]
		[DllImport (Constants.SecurityLibrary)]
		extern static SecStatusCode /* OSStatus */ SecTrustEvaluateAsync (IntPtr /* SecTrustRef */ trust, IntPtr /* dispatch_queue_t */ queue, ref BlockLiteral block);

		internal delegate void TrustEvaluateHandler (IntPtr block, IntPtr trust, SecTrustResult trustResult);
		static readonly TrustEvaluateHandler evaluate = TrampolineEvaluate;

		[MonoPInvokeCallback (typeof (TrustEvaluateHandler))]
		static void TrampolineEvaluate (IntPtr block, IntPtr trust, SecTrustResult trustResult)
		{
			var del = BlockLiteral.GetTarget<SecTrustCallback> (block);
			if (del != null) {
				var t = trust == IntPtr.Zero ? null : new SecTrust (trust);
				del (t, trustResult);
			}
		}

		[iOS (7,0)]
		[Deprecated (PlatformName.MacOSX, 10,15, message: "Use 'Evaluate (DispatchQueue, SecTrustWithErrorCallback)' instead.")]
		[Deprecated (PlatformName.iOS, 13,0, message: "Use 'Evaluate (DispatchQueue, SecTrustWithErrorCallback)' instead.")]
		[Deprecated (PlatformName.WatchOS, 6,0, message: "Use 'Evaluate (DispatchQueue, SecTrustWithErrorCallback)' instead.")]
		[Deprecated (PlatformName.TvOS, 13,0, message: "Use 'Evaluate (DispatchQueue, SecTrustWithErrorCallback)' instead.")]
		// not always async (so suffix is removed)
		[BindingImpl (BindingImplOptions.Optimizable)]
		public SecStatusCode Evaluate (DispatchQueue queue, SecTrustCallback handler)
		{
			// headers have `dispatch_queue_t _Nullable queue` but it crashes... don't trust headers, even for SecTrust
			if (queue == null)
				throw new ArgumentNullException (nameof (queue));
			if (handler == null)
				throw new ArgumentNullException (nameof (handler));

			BlockLiteral block_handler = new BlockLiteral ();
			try {
				block_handler.SetupBlockUnsafe (evaluate, handler);
				return SecTrustEvaluateAsync (handle, queue.Handle, ref block_handler);
			}
			finally {
				block_handler.CleanupBlock ();
			}
		}

		[Watch (6,0), TV (13,0), Mac (10,15), iOS (13,0)]
		[DllImport (Constants.SecurityLibrary)]
		static extern SecStatusCode SecTrustEvaluateAsyncWithError (IntPtr /* SecTrustRef */ trust, IntPtr /* dispatch_queue_t */ queue, ref BlockLiteral block);

		internal delegate void TrustEvaluateErrorHandler (IntPtr block, IntPtr trust, bool result, IntPtr /* CFErrorRef _Nullable */  error);
		static readonly TrustEvaluateErrorHandler evaluate_error = TrampolineEvaluateError;

		[MonoPInvokeCallback (typeof (TrustEvaluateErrorHandler))]
		static void TrampolineEvaluateError (IntPtr block, IntPtr trust, bool result, IntPtr /* CFErrorRef _Nullable */  error)
		{
			var del = BlockLiteral.GetTarget<SecTrustWithErrorCallback> (block);
			if (del != null) {
				var t = trust == IntPtr.Zero ? null : new SecTrust (trust);
				var e = error == IntPtr.Zero ? null : new NSError (error);
				del (t, result, e);
			}
		}

		[Watch (6,0), TV (13,0), Mac (10,15), iOS (13,0)]
		// not always async (so suffix is removed)
		[BindingImpl (BindingImplOptions.Optimizable)]
		public SecStatusCode Evaluate (DispatchQueue queue, SecTrustWithErrorCallback handler)
		{
			if (queue == null)
				throw new ArgumentNullException (nameof (queue));
			if (handler == null)
				throw new ArgumentNullException (nameof (handler));

			BlockLiteral block_handler = new BlockLiteral ();
			try {
				block_handler.SetupBlockUnsafe (evaluate_error, handler);
				return SecTrustEvaluateAsyncWithError (handle, queue.Handle, ref block_handler);
			}
			finally {
				block_handler.CleanupBlock ();
			}
		}

		[iOS (7,0)]
		[DllImport (Constants.SecurityLibrary)]
		extern static SecStatusCode /* OSStatus */ SecTrustGetTrustResult (IntPtr /* SecTrustRef */ trust, out SecTrustResult /* SecTrustResultType */ result);

		[iOS (7,0)]
		public SecTrustResult GetTrustResult ()
		{
			SecTrustResult trust_result;
			SecStatusCode result = SecTrustGetTrustResult (handle, out trust_result);
			if (result != SecStatusCode.Success)
				throw new InvalidOperationException (result.ToString ());
			return trust_result;
		}

		[Watch (5,0)][TV (12,0)][Mac (10,14)][iOS (12,0)]
		[DllImport (Constants.SecurityLibrary)]
		[return: MarshalAs (UnmanagedType.U1)]
		static extern bool SecTrustEvaluateWithError (/* SecTrustRef */ IntPtr trust, out /* CFErrorRef** */ IntPtr error);

		[Watch (5,0)][TV (12,0)][Mac (10,14)][iOS (12,0)]
		public bool Evaluate (out NSError error)
		{
			var result = SecTrustEvaluateWithError (handle, out var err);
			error = err == IntPtr.Zero ? null : new NSError (err);
			return result;
		}

		[iOS (7,0)][Mac (10,9)]
		[DllImport (Constants.SecurityLibrary)]
		extern static IntPtr /* CFDictionaryRef */ SecTrustCopyResult (IntPtr /* SecTrustRef */ trust);

		[iOS (7,0)][Mac (10,9)]
		public NSDictionary GetResult ()
		{
			return new NSDictionary (SecTrustCopyResult (handle), true);
		}

		[iOS (7,0)][Mac (10,9)]
		[DllImport (Constants.SecurityLibrary)]
		extern static SecStatusCode /* OSStatus */ SecTrustSetOCSPResponse (IntPtr /* SecTrustRef */ trust, IntPtr /* CFTypeRef */ responseData);

		// the API accept the handle for a single policy or an array of them
		[Mac (10,9)]
		void SetOCSPResponse (IntPtr ocsp)
		{
			SecStatusCode result = SecTrustSetOCSPResponse (handle, ocsp);
			if (result != SecStatusCode.Success)
				throw new InvalidOperationException (result.ToString ());
		}

		[iOS (7,0)]
		public void SetOCSPResponse (NSData ocspResponse)
		{
			if (ocspResponse == null)
				throw new ArgumentNullException ("ocspResponse");

			SetOCSPResponse (ocspResponse.Handle);
		}

		[iOS (7,0)]
		public void SetOCSPResponse (IEnumerable<NSData> ocspResponses)
		{
			if (ocspResponses == null)
				throw new ArgumentNullException ("ocspResponses");

			using (var array = NSArray.FromNSObjects (ocspResponses.ToArray ()))
				SetOCSPResponse (array.Handle);
		}

		[iOS (7,0)]
		public void SetOCSPResponse (NSArray ocspResponses)
		{
			if (ocspResponses == null)
				throw new ArgumentNullException ("ocspResponses");

			SetOCSPResponse (ocspResponses.Handle);
		}

		[iOS (12,1,1)]
		[Watch (5,1,1)]
		[TV (12,1,1)]
		[Mac (10,14,2)]
		[DllImport (Constants.SecurityLibrary)]
		static extern SecStatusCode /* OSStatus */ SecTrustSetSignedCertificateTimestamps (/* SecTrustRef* */ IntPtr trust, /* CFArrayRef* */ IntPtr sctArray);

		[iOS (12,1,1)]
		[Watch (5,1,1)]
		[TV (12,1,1)]
		[Mac (10,14,2)]
		public SecStatusCode SetSignedCertificateTimestamps (IEnumerable<NSData> sct)
		{
			if (sct == null)
				return SecTrustSetSignedCertificateTimestamps (handle, IntPtr.Zero);

			using (var array = NSArray.FromNSObjects (sct.ToArray ()))
				return SecTrustSetSignedCertificateTimestamps (handle, array.Handle);
		}

		[iOS (12,1,1)]
		[Watch (5,1,1)]
		[TV (12,1,1)]
		[Mac (10,14,2)]
		public SecStatusCode SetSignedCertificateTimestamps (NSArray<NSData> sct)
		{
			return SecTrustSetSignedCertificateTimestamps (handle, sct.GetHandle ());
		}
	}
}