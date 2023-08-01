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

#if NET6_0_OR_GREATER
using System.Diagnostics.CodeAnalysis;
#endif
using System.Reflection;

namespace OpenTelemetry.Instrumentation;

/// <summary>
/// PropertyFetcher fetches a property from an object.
/// </summary>
/// <typeparam name="T">The type of the property being fetched.</typeparam>
internal sealed class PropertyFetcher<T>
{
#if NET6_0_OR_GREATER
    private const string TrimCompatibilityMessage = "PropertyFetcher is used to access properties on objects dynamically by design and cannot be made trim compatible.";
#endif
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
#if NET6_0_OR_GREATER
    [RequiresUnreferencedCode(TrimCompatibilityMessage)]
#endif
    public bool TryFetch(object obj, out T value, bool skipObjNullCheck = false)
    {
        if (!skipObjNullCheck && obj == null)
        {
            value = default;
            return false;
        }

        if (this.innerFetcher == null)
        {
            this.innerFetcher = PropertyFetch.Create(obj.GetType().GetTypeInfo(), this.propertyName);
        }

        if (this.innerFetcher == null)
        {
            value = default;
            return false;
        }

        value = this.innerFetcher.Fetch(obj);
        return true;
    }

    // see https://github.com/dotnet/corefx/blob/master/src/System.Diagnostics.DiagnosticSource/src/System/Diagnostics/DiagnosticSourceEventSource.cs
#if NET6_0_OR_GREATER
    [RequiresUnreferencedCode(TrimCompatibilityMessage)]
#endif
    private abstract class PropertyFetch
    {
        public static PropertyFetch Create(TypeInfo type, string propertyName)
        {
            PropertyInfo propertyInfo = type.DeclaredProperties.FirstOrDefault(p => string.Equals(p.Name, propertyName, StringComparison.OrdinalIgnoreCase)) ?? type.GetProperty(propertyName);
            if (propertyInfo == null)
            {
                // returns null and wait for a valid payload to arrive.
                return null;
            }

            if (typeof(T) != propertyInfo.PropertyType)
            {
                throw new NotSupportedException(
                    $"PropertyFetcher doesn't support fetching values of properties of different type then declared." +
                    $"The expected type of property {type.FullName}.{propertyName} is {typeof(T)} but the actual type is {propertyInfo.PropertyType}.");
            }

            Type declaringType = propertyInfo.DeclaringType;
            if (declaringType.IsValueType)
            {
                throw new NotSupportedException(
                    $"PropertyFetcher can only operate on reference payload types." +
                    $"Type {declaringType.FullName} is a value type though.");
            }

            if (declaringType == typeof(object))
            {
                // This is only necessary on .NET 7. In .NET 8 the compiler is improved and using the MakeGenericMethod will work on its own.
                // The reason is to force the compiler to create an instantiation of the method with a reference type.
                // The code for that instantiation can then be reused at runtime to create instantiation over any other reference.
                return CreateInstantiated<object>(propertyInfo);
            }
            else
            {
                return DynamicInstantiationHelper(declaringType, propertyInfo);
            }

            // Separated as local function to be able to target the suppression to just this call
            // IL3050 was generated here because of the call to MakeGenericType, which is problematic in AOT if one of the type parameters is a value type;
            // because the compiler might need to generate code specific to that type.
            // If the type parameter is reference type, there will be no problem; because the generated code can be shared among all reference type instantiations.
#if NET6_0_OR_GREATER
            [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "The code guarantees that all the generic parameters are reference types")]
#endif
            static PropertyFetch DynamicInstantiationHelper(Type declaringType, PropertyInfo propertyInfo)
            {
                return (PropertyFetch)typeof(PropertyFetch)
                    .GetMethod(nameof(CreateInstantiated), BindingFlags.NonPublic | BindingFlags.Static)!
                    .MakeGenericMethod(declaringType) // This is validated above that it's a reference type
                    .Invoke(null, new object[] { propertyInfo });
            }
        }

        public abstract T Fetch(object obj);

        private static PropertyFetch CreateInstantiated<TDeclaredObject>(PropertyInfo propertyInfo)
            where TDeclaredObject : class
            => new PropertyFetchInstantiated<TDeclaredObject>(propertyInfo);

        private sealed class PropertyFetchInstantiated<TDeclaredObject> : PropertyFetch
            where TDeclaredObject : class
        {
            private readonly Func<TDeclaredObject, T> propertyFetch;

            public PropertyFetchInstantiated(PropertyInfo property)
            {
                this.propertyFetch = (Func<TDeclaredObject, T>)property.GetMethod.CreateDelegate(typeof(Func<TDeclaredObject, T>));
            }

            public override T Fetch(object obj)
            {
                if (obj is not TDeclaredObject o)
                {
                    throw new NotSupportedException($"PropertyFetcher called on two different payload object types. First was {typeof(TDeclaredObject).FullName} and second was {obj.GetType().FullName}");
                }

                return this.propertyFetch(o);
            }
        }
    }
}
