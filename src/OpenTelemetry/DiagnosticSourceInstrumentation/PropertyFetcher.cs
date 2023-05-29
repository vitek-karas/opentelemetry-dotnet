// <copyright file="PropertyFetcher.cs" company="OpenTelemetry Authors">
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

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
#pragma warning restore IDE0005

namespace OpenTelemetry.Instrumentation
{
    /// <summary>
    /// PropertyFetcher fetches a property from an object.
    /// </summary>
    /// <typeparam name="T">The type of the property being fetched.</typeparam>
    internal sealed class PropertyFetcher<T>
    {
        private const DynamicallyAccessedMemberTypes AllProperties = DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.NonPublicProperties;

        private readonly string propertyName;
        private PropertyFetch innerFetcher;

        /// <summary>
        /// Initializes a new instance of the <see cref="PropertyFetcher{T}"/> class.
        /// </summary>
        /// <param name="propertyName">Property name to fetch.</param>
        public PropertyFetcher(string propertyName)
        {
            this.propertyName = propertyName;
        }

        /// <summary>
        /// Try to fetch the property from the object.
        /// </summary>
        /// <param name="obj">Object to be fetched.</param>
        /// <param name="value">Fetched value.</param>
        /// <param name="skipObjNullCheck">Set this to <see langword= "true"/> if we know <paramref name="obj"/> is not <see langword= "null"/>.</param>
        /// <returns><see langword= "true"/> if the property was fetched.</returns>
        [RequiresUnreferencedCode("Allows access to properties on random object which is not trim compatible")]
        public bool TryFetch(object obj, out T value, bool skipObjNullCheck = false)
        {
            if (!skipObjNullCheck && obj == null)
            {
                value = default;
                return false;
            }

            if (this.innerFetcher == null)
            {
                this.innerFetcher = PropertyFetch.Create(obj, this.propertyName);
            }

            if (this.innerFetcher == null)
            {
                value = default;
                return false;
            }

            return this.innerFetcher.TryFetch(obj, out value);
        }

        public bool TryFetch([DynamicallyAccessedMembers(AllProperties)] Type objectType, object obj, out T value, bool skipObjNullCheck = false)
        {
            if (!skipObjNullCheck && obj == null)
            {
                value = default;
                return false;
            }

            if (this.innerFetcher == null)
            {
                this.innerFetcher = PropertyFetch.Create(objectType.GetTypeInfo(), this.propertyName);
            }

            if (this.innerFetcher == null)
            {
                value = default;
                return false;
            }

            return this.innerFetcher.TryFetch(obj, out value);
        }

        // see https://github.com/dotnet/corefx/blob/master/src/System.Diagnostics.DiagnosticSource/src/System/Diagnostics/DiagnosticSourceEventSource.cs
        private class PropertyFetch
        {
            public static PropertyFetch Create([DynamicallyAccessedMembers(AllProperties)] TypeInfo type, string propertyName)
            {
                return Create(type, propertyName, null);
            }

            [RequiresUnreferencedCode("Allows access to properties on random object which is not trim compatible")]
            public static PropertyFetch Create(object obj, string propertyName)
            {
                return Create(obj.GetType().GetTypeInfo(), propertyName, FallbackPropertyFetchCreator);

                static PropertyFetch FallbackPropertyFetchCreator(object obj, string propertyName)
                {
                    return Create(obj, propertyName);
                }
            }

            public virtual bool TryFetch(object obj, out T value)
            {
                value = default;
                return false;
            }

            // TODO: REMOVE - this is NOT safe
            [UnconditionalSuppressMessage("AOT", "IL3050:MakeGenericType")]
            private static PropertyFetch Create([DynamicallyAccessedMembers(AllProperties)] TypeInfo type, string propertyName, Func<object, string, PropertyFetch> fallbackPropertyFetchCreator)
            {
                var property = type.DeclaredProperties.FirstOrDefault(p => string.Equals(p.Name, propertyName, StringComparison.OrdinalIgnoreCase));
                if (property == null)
                {
                    property = type.GetProperty(propertyName);
                }

                return CreateFetcherForProperty(property, fallbackPropertyFetchCreator);

                static PropertyFetch CreateFetcherForProperty(PropertyInfo propertyInfo, Func<object, string, PropertyFetch> fallbackPropertyFetchCreator)
                {
                    if (propertyInfo == null || !typeof(T).IsAssignableFrom(propertyInfo.PropertyType))
                    {
                        // returns null and wait for a valid payload to arrive.
                        return null;
                    }

                    var typedPropertyFetcher = typeof(TypedPropertyFetch<>);
                    var instantiatedTypedPropertyFetcher = typedPropertyFetcher.MakeGenericType(
                        typeof(T), propertyInfo.DeclaringType);
                    return (PropertyFetch)Activator.CreateInstance(instantiatedTypedPropertyFetcher, propertyInfo, fallbackPropertyFetchCreator);
                }
            }

            private sealed class TypedPropertyFetch<TDeclaredObject> : PropertyFetch
            {
                private readonly string propertyName;
                private readonly Func<TDeclaredObject, T> propertyFetch;
                private readonly Func<object, string, PropertyFetch> fallbackPropertyFetchCreator;

                private PropertyFetch innerFetcher;

                public TypedPropertyFetch(PropertyInfo property, Func<object, string, PropertyFetch> fallbackPropertyFetchCreator)
                {
                    this.propertyName = property.Name;
                    this.propertyFetch = (Func<TDeclaredObject, T>)property.GetMethod.CreateDelegate(typeof(Func<TDeclaredObject, T>));
                    this.fallbackPropertyFetchCreator = fallbackPropertyFetchCreator;
                }

                public override bool TryFetch(object obj, out T value)
                {
                    if (obj is TDeclaredObject o)
                    {
                        value = this.propertyFetch(o);
                        return true;
                    }

                    if (this.fallbackPropertyFetchCreator != null)
                    {
                        this.innerFetcher ??= this.fallbackPropertyFetchCreator(obj, this.propertyName);
                        if (this.innerFetcher != null)
                        {
                            return this.innerFetcher.TryFetch(obj, out value);
                        }
                    }

                    value = default;
                    return false;
                }
            }
        }
    }
}
