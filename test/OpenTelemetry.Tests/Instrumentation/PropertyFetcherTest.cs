// <copyright file="PropertyFetcherTest.cs" company="OpenTelemetry Authors">
// Copyright The OpenTelemetry Authors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// </copyright>

using System.Diagnostics;
using Xunit;

namespace OpenTelemetry.Instrumentation.Tests
{
    public class PropertyFetcherTest
    {
        [Fact]
        public void FetchValidProperty()
        {
            using var activity = new Activity("test");
            var fetch = new PropertyFetcher<string>("DisplayName");
            Assert.True(fetch.TryFetch(activity, out string result));
            Assert.Equal(activity.DisplayName, result);
        }

        [Fact]
        public void FetchInvalidProperty()
        {
            using var activity = new Activity("test");
            var fetch = new PropertyFetcher<string>("DisplayName2");
            Assert.False(fetch.TryFetch(activity, out string result));

            var fetchInt = new PropertyFetcher<int>("DisplayName2");
            Assert.False(fetchInt.TryFetch(activity, out int resultInt));

            Assert.Equal(default, result);
            Assert.Equal(default, resultInt);
        }

        [Fact]
        public void FetchNullProperty()
        {
            var fetch = new PropertyFetcher<string>("null");
            Assert.False(fetch.TryFetch(null, out _));
        }

        [Fact]
        public void FetchPropertyMultiplePayloadTypes()
        {
            var fetch = new PropertyFetcher<string>("Property");

            Assert.True(fetch.TryFetch(new PayloadTypeA(), out string propertyValue));
            Assert.Equal("A", propertyValue);

            Assert.True(fetch.TryFetch(new PayloadTypeB(), out propertyValue));
            Assert.Equal("B", propertyValue);

            Assert.False(fetch.TryFetch(new PayloadTypeC(), out _));

            Assert.False(fetch.TryFetch(null, out _));
        }

        [Fact]
        public void FetchPropertyMultiplePayloadTypes_IgnoreTypesWithoutExpectedPropertyName()
        {
            var fetch = new PropertyFetcher<string>("Property");

            Assert.False(fetch.TryFetch(new PayloadTypeC(), out _));

            Assert.True(fetch.TryFetch(new PayloadTypeA(), out string propertyValue));
            Assert.Equal("A", propertyValue);
        }

        [Fact]
        public void FetchPropertyMultiplePayloadValueTypes()
        {
            var fetch = new PropertyFetcher<ValuePropertyType>("Property");

            Assert.True(fetch.TryFetch(new ReferencePayloadWithValueProperty(), out ValuePropertyType propertyValueFromReferencePayload));
            Assert.Equal(42, propertyValueFromReferencePayload.Value);

            Assert.True(fetch.TryFetch(new ValuePayloadWithValueProperty(), out ValuePropertyType propertyValueFromValuePayload));
            Assert.Equal(43, propertyValueFromValuePayload.Value);
        }

        private struct ValuePropertyType
        {
            public int Value;
        }

        private struct ValuePayloadWithValueProperty
        {
            public ValuePayloadWithValueProperty()
            {
                this.Property = new ValuePropertyType() { Value = 43 };
            }

            public ValuePropertyType Property { get; set; }
        }

        private class PayloadTypeA
        {
            public string Property { get; set; } = "A";
        }

        private class PayloadTypeB
        {
            public string Property { get; set; } = "B";
        }

        private class PayloadTypeC
        {
        }

        private class ReferencePayloadWithValueProperty
        {
            public ValuePropertyType Property { get; set; } = new ValuePropertyType() { Value = 42 };
        }
    }
}
