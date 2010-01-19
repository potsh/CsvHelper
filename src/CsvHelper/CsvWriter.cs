﻿#region License
// Copyright 2009-2010 Josh Close
// This file is a part of CsvHelper and is licensed under the MS-PL
// See LICENSE.txt for details or visit http://www.opensource.org/licenses/ms-pl.html
#endregion
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace CsvHelper
{
	/// <summary>
	/// Used to write CSV files.
	/// </summary>
	public class CsvWriter : ICsvWriter
	{
		private bool disposed;
		private char delimiter = ',';
		private readonly List<string> currentRecord = new List<string>();
		private StreamWriter writer;
		private bool hasHeaderBeenWritten;
		private readonly Dictionary<Type, PropertyInfo[]> typeProperties = new Dictionary<Type, PropertyInfo[]>();
		private readonly Dictionary<Type, Delegate> typeActions = new Dictionary<Type, Delegate>();

		/// <summary>
		/// Gets or sets the delimiter used to
		/// separate the fields of the CSV records.
		/// </summary>
		public virtual char Delimiter
		{
			get { return delimiter; }
			set { delimiter = value; }
		}

		/// <summary>
		/// Gets are sets a value indicating if the
		/// CSV file has a header record.
		/// </summary>
		public virtual bool HasHeaderRecord { get; set; }

		/// <summary>
		/// Creates a new CSV writer using the given <see cref="StreamWriter" />.
		/// </summary>
		/// <param name="writer">The writer used to write the CSV file.</param>
		public CsvWriter( StreamWriter writer )
		{
			this.writer = writer;
		}

		/// <summary>
		/// Creates a new CSV writer using the given file path.
		/// </summary>
		/// <param name="filePath">The file path used to write the CSV file.</param>
		public CsvWriter( string filePath ) : this( new StreamWriter( filePath ) ) { }

		/// <summary>
		/// Writes the field to the CSV file.
		/// When all fields are written for a record,
		/// <see cref="ICsvWriter.NextRecord" /> must be called
		/// to complete writing of the current record.
		/// </summary>
		/// <param name="field">The field to write.</param>
		public virtual void WriteField( string field )
		{
			if( !string.IsNullOrEmpty( field ) )
			{
				var hasQuote = false;
				if( field.Contains( "\"" ) )
				{
					// All quotes must be doubled.
					field = field.Replace( "\"", "\"\"" );
					hasQuote = true;
				}
				if( hasQuote ||
					field[0] == ' ' ||
					field[field.Length - 1] == ' ' ||
					field.Contains( delimiter.ToString() ) ||
					field.Contains( "\n" ) )
				{
					// Surround the field in double quotes.
					field = string.Format( "\"{0}\"", field );
				}
			}

			currentRecord.Add( field );
		}

		/// <summary>
		/// Writes the field to the CSV file.
		/// When all fields are written for a record,
		/// <see cref="ICsvWriter.NextRecord" /> must be called
		/// to complete writing of the current record.
		/// </summary>
		/// <typeparam name="T">The type of the field.</typeparam>
		/// <param name="field">The field to write.</param>
		public virtual void WriteField<T>( T field )
		{
			CheckDisposed();

			var type = typeof( T );
			if( type == typeof( string ) )
			{
				WriteField( field as string );
			}
			else if( type.IsValueType )
			{
				WriteField( field.ToString() );
			}
			else
			{
				var converter = TypeDescriptor.GetConverter( typeof( T ) );
				WriteField( field, converter );
			}
		}

		/// <summary>
		/// Writes the field to the CSV file.
		/// When all fields are written for a record,
		/// <see cref="ICsvWriter.NextRecord" /> must be called
		/// to complete writing of the current record.
		/// </summary>
		/// <typeparam name="T">The type of the field.</typeparam>
		/// <param name="field">The field to write.</param>
		/// <param name="converter">The converter used to convert the field into a string.</param>
		public virtual void WriteField<T>( T field, TypeConverter converter )
		{
			CheckDisposed();

			var fieldString = converter.ConvertToString( field );
			WriteField( fieldString );
		}

		/// <summary>
		/// Ends writing of the current record
		/// and starts a new record. This is used
		/// when manually writing records with <see cref="ICsvWriter.WriteField{T}" />
		/// </summary>
		public virtual void NextRecord()
		{
			CheckDisposed();

			var record = string.Join( delimiter.ToString(), currentRecord.ToArray() );
			writer.WriteLine( record );
			writer.Flush();
			currentRecord.Clear();
		}

		/// <summary>
		/// Writes the record to the CSV file.
		/// </summary>
		/// <typeparam name="T">The type of the record.</typeparam>
		/// <param name="record">The record to write.</param>
		public virtual void WriteRecord<T>( T record )
		{
			CheckDisposed();

			if( HasHeaderRecord && !hasHeaderBeenWritten )
			{
				WriteHeader( GetProperties<T>() );
			}

			GetAction<T>()( this, record );

			NextRecord();
		}

		/// <summary>
		/// Writes the list of records to the CSV file.
		/// </summary>
		/// <typeparam name="T">The type of the record.</typeparam>
		/// <param name="records">The list of records to write.</param>
		public virtual void WriteRecords<T>( IEnumerable<T> records )
		{
			CheckDisposed();

			if( HasHeaderRecord && !hasHeaderBeenWritten )
			{
				WriteHeader( GetProperties<T>() );
			}

			foreach( var record in records )
			{
				GetAction<T>()( this, record );
				NextRecord();
			}
		}

		/// <summary>
		/// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
		/// </summary>
		/// <filterpriority>2</filterpriority>
		public virtual void Dispose()
		{
			Dispose( true );
			GC.SuppressFinalize( this );
		}

		protected virtual void Dispose( bool disposing )
		{
			if( !disposed )
			{
				if( disposing )
				{
					if( writer != null )
					{
						writer.Dispose();
					}
				}

				disposed = true;
				writer = null;
			}
		}

		protected virtual void CheckDisposed()
		{
			if( disposed )
			{
				throw new ObjectDisposedException( GetType().ToString() );
			}
		}

		protected virtual void WriteHeader( PropertyInfo[] properties )
		{
			foreach( var property in properties )
			{
				var fieldName = property.Name;
				var csvFieldAttribute = ReflectionHelper.GetAttribute<CsvFieldAttribute>( property, false );
				if( csvFieldAttribute != null && !string.IsNullOrEmpty( csvFieldAttribute.FieldName ) )
				{
					fieldName = csvFieldAttribute.FieldName;
				}
				if( csvFieldAttribute == null || !csvFieldAttribute.Ignore )
				{
					WriteField( fieldName );
				}
			}
			NextRecord();
			hasHeaderBeenWritten = true;
		}

		protected virtual PropertyInfo[] GetProperties<T>()
		{
			var type = typeof( T );
			if( !typeProperties.ContainsKey( type ) )
			{
				var properties = type.GetProperties();
				var shouldSort = properties.Any( property =>
				{
					// Only sort if there is at least one attribute
					// that has the field index specified.
					var csvFieldAttribute = ReflectionHelper.GetAttribute<CsvFieldAttribute>( property, false );
					return csvFieldAttribute != null && csvFieldAttribute.FieldIndex >= 0;
				} );
				if( shouldSort )
				{
					Array.Sort( properties, new CsvPropertyInfoComparer( false ) );
				}
				typeProperties[type] = properties;
			}
			return typeProperties[type];
		}

		protected virtual Action<CsvWriter, T> GetAction<T>()
		{
			var type = typeof( T );

			if( !typeActions.ContainsKey( type ) )
			{
				var properties = GetProperties<T>();

				Action<CsvWriter, T> func = null;
				var writerParameter = Expression.Parameter( typeof( CsvWriter ), "writer" );
				var recordParameter = Expression.Parameter( typeof( T ), "record" );
				foreach( var property in properties )
				{
					var csvFieldAttribute = ReflectionHelper.GetAttribute<CsvFieldAttribute>( property, false );
					if( csvFieldAttribute != null && csvFieldAttribute.Ignore )
					{
						// Skip this property.
						continue;
					}

					TypeConverter typeConverter = null;
					var typeConverterAttribute = ReflectionHelper.GetAttribute<TypeConverterAttribute>( property, false );
					if( typeConverterAttribute != null )
					{
						var typeConverterType = Type.GetType( typeConverterAttribute.ConverterTypeName );
						if( typeConverterType != null )
						{
							typeConverter = Activator.CreateInstance( typeConverterType ) as TypeConverter;
						}
					}

					Expression fieldExpression = Expression.Property( recordParameter, property );
					if( typeConverter != null )
					{
						// Convert the property value to a string using the
						// TypeConverter specified in the TypeConverterAttribute.
						var typeConverterExpression = Expression.Constant( typeConverter );
						var method = typeConverter.GetType().GetMethod( "ConvertToString", new[] { typeof( object ) } );
						fieldExpression = Expression.Convert( fieldExpression, typeof( object ) );
						fieldExpression = Expression.Call( typeConverterExpression, method, fieldExpression );
					}
					else if( property.PropertyType != typeof( string ) )
					{
						if( property.PropertyType.IsValueType )
						{
							// Convert the property value to a string using ToString.
							fieldExpression = Expression.Call( fieldExpression, "ToString", null, null );
						}
						else
						{
							// Convert the property value to a string using
							// the default TypeConverter for the properties type.
							typeConverter = TypeDescriptor.GetConverter( property.PropertyType );
							var method = typeConverter.GetType().GetMethod( "ConvertToString", new[] { typeof( object ) } );
							var typeConverterExpression = Expression.Constant( typeConverter );
							fieldExpression = Expression.Convert( fieldExpression, typeof( object ) );
							fieldExpression = Expression.Call( typeConverterExpression, method, fieldExpression );
						}
					}

					var body = Expression.Call( writerParameter, "WriteField", new[] { typeof( string ) }, fieldExpression );
					func += Expression.Lambda<Action<CsvWriter, T>>( body, writerParameter, recordParameter ).Compile();
				}

				typeActions[type] = func;
			}

			return  (Action<CsvWriter, T>)typeActions[type];
		}
	}
}