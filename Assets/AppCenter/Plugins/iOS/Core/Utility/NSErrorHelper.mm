// Copyright (c) Microsoft Corporation. All rights reserved.
//
// Licensed under the MIT license.

#import "NSStringHelper.h"

const char* app_center_unity_nserror_domain(void* error)
{
  NSError *nsError = (__bridge NSError*)error;
  return appcenter_unity_ns_string_to_cstr([nsError domain]);
}

const long app_center_unity_nserror_code(void* error)
{
  NSError *nsError = (__bridge NSError*)error;
  return [nsError code];
}
