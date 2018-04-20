using System;
using JetBrains.Annotations;
using JetBrains.Application.Progress;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Feature.Services.ContextActions;
using JetBrains.ReSharper.Feature.Services.CSharp.Analyses.Bulbs;
using JetBrains.ReSharper.Psi.CodeStyle;
using JetBrains.ReSharper.Psi.CSharp;
using JetBrains.ReSharper.Psi.CSharp.Tree;
using JetBrains.ReSharper.Psi.CSharp.Util;
using JetBrains.ReSharper.Psi.Tree;
using JetBrains.TextControl;
using JetBrains.Util;

namespace AddingLogging
{
    [UsedImplicitly]
    [ContextAction(Group = "C#", Name = "AddingLogging", Description = "text")]
    public class AddingLoggingContextAction : ContextActionBase
    {
        [NotNull] private readonly ICSharpContextActionDataProvider provider;

        public AddingLoggingContextAction([NotNull] ICSharpContextActionDataProvider provider)
        {
            this.provider = provider ?? throw new ArgumentNullException(paramName: nameof(provider));
        }

        protected override Action<ITextControl> ExecutePsiTransaction(ISolution solution, IProgressIndicator progress)
        {
            IMethodDeclaration method_declaration = this.provider.GetSelectedElement<IMethodDeclaration>();
            if (method_declaration == null) return null;

            String start_begin_s = "Begin";
            String start_return_s = "End";
            String start_exception_s = default;
            String start_throw_s = "Throw";

            IClassBody class_body = default;

            if (method_declaration.Parent is IClassBody class_body_local)
            {
                class_body = class_body_local;
                if (class_body_local.Parent is IClassDeclaration class_declaration)
                {
                    foreach (IAttribute attribute in class_declaration.Attributes)
                    {
                        if (attribute.Name.GetText() == "AddingLogging")
                        {
                            foreach (IPropertyAssignment property_assignment in attribute.PropertyAssignments)
                            {
                                switch (property_assignment.PropertyNameIdentifier.Name)
                                {
                                    case "BeginCode":
                                    {
                                        if (property_assignment.Source is ICSharpLiteralExpression literal_expression)
                                        {
                                            start_begin_s = literal_expression.Literal.GetText().Replace(oldValue: "\"", newValue: default);
                                        }
                                    }
                                        break;
                                    case "ReturnCode":
                                    {
                                        if (property_assignment.Source is ICSharpLiteralExpression literal_expression)
                                        {
                                            start_return_s = literal_expression.Literal.GetText().Replace(oldValue: "\"", newValue: default);
                                        }
                                    }
                                        break;
                                    case "ExceptionCode":
                                    {
                                        if (property_assignment.Source is ICSharpLiteralExpression literal_expression)
                                        {
                                            start_exception_s = literal_expression.Literal.GetText().Replace(oldValue: "\"", newValue: default);
                                        }
                                    }
                                        break;
                                    case "ThrowCode":
                                    {
                                        if (property_assignment.Source is ICSharpLiteralExpression literal_expression)
                                        {
                                            start_throw_s = literal_expression.Literal.GetText().Replace(oldValue: "\"", newValue: default);
                                        }
                                    }
                                        break;
                                }
                            }
                        }
                    }
                }
            }

            String tag_string = default;
            if (class_body != null)
            {
                foreach (IMultipleFieldDeclaration field_declaration in class_body.FieldDeclarations)
                {
                    if (field_declaration?.Attributes?.Attributes == null) continue;
                    foreach (IAttribute attributes_attribute in field_declaration.Attributes.Attributes)
                    {
                        if (attributes_attribute.Name.GetText() == "AddingLoggingTag")
                        {
                            if (String.IsNullOrWhiteSpace(value: tag_string)) tag_string = "{{";
                            foreach (IMultipleDeclarationMember multiple_declaration_member in field_declaration.Declarators)
                            {
                                tag_string += $"{{{multiple_declaration_member.DeclaredElement?.ShortName}}};";
                            }
                            break;
                        }
                    }
                }
            }
            if (!String.IsNullOrWhiteSpace(value: tag_string))
            {
                tag_string = tag_string.TrimEnd(';');
                tag_string += "}} ";
            }

            CSharpElementFactory c_sharp_element_factory = CSharpElementFactory.GetInstance(element: method_declaration);
            String begin_code_s = "Log.Instance.ForMethod().Debug($\"%Tag%%Start% {nameof(%MethodName%)}"
                .Replace(oldValue: "%MethodName%", newValue: method_declaration.DeclaredName)
                .Replace(oldValue: "%Start%", newValue: start_begin_s)
                .Replace(oldValue: "%Tag%", newValue: tag_string);
            String return_code_s = "Log.Instance.ForMethod().Debug($\"%Tag%%Start% {nameof(%MethodName%)}: Return "
                .Replace(oldValue: "%MethodName%", newValue: method_declaration.DeclaredName)
                .Replace(oldValue: "%Start%", newValue: start_return_s)
                .Replace(oldValue: "%Tag%", newValue: tag_string);
            String exception_code_s = String.IsNullOrWhiteSpace(value: start_exception_s)
                ? "Log.Instance.ForMethod().Error("
                : "Log.Instance.ForMethod().Error(\"%Tag%%Start%\", "
                    .Replace(oldValue: "%Start%", newValue: start_exception_s)
                    .Replace(oldValue: "%Tag%", newValue: tag_string);
            String throw_code_s = "Log.Instance.ForMethod().Warn($\"%Tag%%Start% {nameof("
                .Replace(oldValue: "%Start%", newValue: start_throw_s)
                .Replace(oldValue: "%Tag%", newValue: tag_string);

            if (method_declaration.Params.ParameterDeclarations.Any())
            {
                begin_code_s = AddParams(code_s: begin_code_s, method_declaration: method_declaration);
            }
            begin_code_s += "\");";

            ICSharpStatement sharp_statement = c_sharp_element_factory.CreateStatement(format: begin_code_s);
            IBlock block = method_declaration.Body;
            if (block.Statements.Any())
            {
                if (block.Statements.First().GetText().StartsWith(value: "Log.") != true)
                {
                    block.AddStatementBefore(statement: sharp_statement, anchor: block.Statements.First());
                }
            }
            foreach (ITreeNode c_sharp_statement in block.Descendants())
            {
                switch (c_sharp_statement)
                {
                    case IReturnStatement return_statement:
                    {
                        //обработка return
                        String local_return_s = return_code_s + (return_statement.Value != null ? $"{return_statement.Value.GetText()}={{{return_statement.Value.GetText()}}}\");" : "void\");");
                        ICSharpStatement statement = c_sharp_element_factory.CreateStatement(format: local_return_s);
                        ICSharpStatement previous_statement = return_statement.GetPreviousStatement();
                        if (previous_statement?.GetText().StartsWith(value: "Log.") != true)
                        {
                            if (return_statement.Parent is IIfStatement if_statement)
                            {
                                IBlock new_block = c_sharp_element_factory.CreateBlock(format: $"{{{local_return_s}}}");
                                new_block.AddStatementAfter(statement: return_statement, anchor: new_block.Statements.First());
                                if_statement.Then.ReplaceWithBlock(blockToInsert: new_block);
                            }
                            else
                            {
                                block.AddStatementBefore(statement: statement, anchor: return_statement);
                            }
                        }
                    }
                        break;

                    case ITryStatement try_statement:
                    {
                        //обработка блока try
                        foreach (ICatchClause catch_clause in try_statement.CatchesEnumerable)
                        {
                            if (catch_clause is ISpecificCatchClause specific_catch_clause)
                            {
                                String local_exception_s = exception_code_s + $"{specific_catch_clause.ExceptionDeclaration.DeclaredElement.ShortName});";
                                ICSharpStatement statement = c_sharp_element_factory.CreateStatement(format: local_exception_s);
                                IBlock body = specific_catch_clause.Body;
                                if (body.Statements.Any())
                                {
                                    if (body.Statements.First()?.GetText().StartsWith(value: "Log.") != true)
                                    {
                                        body.AddStatementBefore(statement: statement, anchor: body.Statements.First());
                                    }
                                }
                                else
                                {
                                    IBlock new_block = c_sharp_element_factory.CreateBlock(format: $"{{{local_exception_s}}}");
                                    specific_catch_clause.SetBody(param: new_block);
                                }
                            }
                        }
                    }
                        break;

                    case IThrowStatement throw_statement:
                    {
                        //обработка выброса ошибки
                        if (throw_statement.Exception is IObjectCreationExpression creation_expression)
                        {
                            String local_throw_s = throw_code_s + $"{creation_expression.TypeName.QualifiedName})}}.";
                            if (creation_expression.Arguments.Any())
                            {
                                foreach (ICSharpArgument c_sharp_argument in creation_expression.Arguments)
                                {
                                    if (c_sharp_argument.Value.NodeType.Index == 2029 && c_sharp_argument.Value is ICSharpLiteralExpression literal_expression)
                                    {
                                        local_throw_s += $" {literal_expression.GetText().Replace(oldValue: "\"", newValue: default)}.";
                                    }
                                    if (c_sharp_argument.Value is IObjectCreationExpression object_creation_expression)
                                    {
                                        foreach (ICSharpArgument sharp_argument in object_creation_expression.Arguments)
                                        {
                                            if (sharp_argument.Value.NodeType.Index == 2029 && sharp_argument.Value is ICSharpLiteralExpression c_sharp_literal_expression)
                                            {
                                                local_throw_s += $" {c_sharp_literal_expression.GetText().Replace(oldValue: "\"", newValue: default)}.";
                                            }
                                        }
                                    }
                                }
                            }
                            local_throw_s += "\");";
                            ICSharpStatement statement = c_sharp_element_factory.CreateStatement(format: local_throw_s);
                            if (throw_statement.GetPreviousStatement()?.GetText().StartsWith(value: "Log.") != true)
                            {
                                block.AddStatementBefore(statement: statement, anchor: throw_statement);
                            }
                        }
                    }
                        break;
                }
            }
            return null;
        }

        private String AddParams(String code_s, IMethodDeclaration method_declaration)
        {
            String params_s = ":";
            foreach (ICSharpParameterDeclaration parameter_declaration in method_declaration.Params.ParameterDeclarationsEnumerable)
            {
                String parameter_s = $"{parameter_declaration.GetText()}={{{parameter_declaration.NameIdentifier?.GetText()}}}";
                params_s += $" {parameter_s};";
            }
            return code_s + params_s;
        }

        public override String Text { get; } = "Adding Logging to the Method";

        public override Boolean IsAvailable(IUserDataHolder cache)
        {
            IMethodDeclaration method_declaration = this.provider.GetSelectedElement<IMethodDeclaration>();
            return method_declaration != null;
        }
    }
}