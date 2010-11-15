using System.Collections.Generic;
using System.Linq;
using ICSharpCode.NRefactory.Ast;
using ICSharpCode.NRefactory.Visitors;

namespace Raven.Database.Linq
{
    public class CaptureSelectNewFieldNamesVisitor : AbstractAstVisitor
    {
        public HashSet<string> FieldNames = new HashSet<string>();

        public override object VisitQueryExpressionSelectClause(QueryExpressionSelectClause queryExpressionSelectClause,
                                                                object data)
        {
            ProcessQuery(queryExpressionSelectClause.Projection);
            return base.VisitQueryExpressionSelectClause(queryExpressionSelectClause, data);
        }

        public override object VisitInvocationExpression(InvocationExpression invocationExpression, object data)
        {
            var memberReferenceExpression = invocationExpression.TargetObject as MemberReferenceExpression;

            if (memberReferenceExpression == null)
                return base.VisitInvocationExpression(invocationExpression, data);

            LambdaExpression lambdaExpression;
            switch (memberReferenceExpression.MemberName)
            {
                case "Select":
                    if (invocationExpression.Arguments.Count != 1)
                        return base.VisitInvocationExpression(invocationExpression, data);
                    lambdaExpression = invocationExpression.Arguments[0] as LambdaExpression;
                    break;
                case "SelectMany":
                    if (invocationExpression.Arguments.Count != 2)
                        return base.VisitInvocationExpression(invocationExpression, data);
                    lambdaExpression = invocationExpression.Arguments[1] as LambdaExpression;
                    break;
                default:
                    return base.VisitInvocationExpression(invocationExpression, data);
            }

            if (lambdaExpression == null)
                return base.VisitInvocationExpression(invocationExpression, data);

            ProcessQuery(lambdaExpression.ExpressionBody);

            return base.VisitInvocationExpression(invocationExpression, data);
        }

        private void ProcessQuery(Expression queryExpressionSelectClause)
        {
            var objectCreateExpression = queryExpressionSelectClause as ObjectCreateExpression;
            if (objectCreateExpression == null ||
                objectCreateExpression.IsAnonymousType == false)
                return;

            foreach (
                var expression in
                    objectCreateExpression.ObjectInitializer.CreateExpressions.OfType<NamedArgumentExpression>())
            {
                FieldNames.Add(expression.Name);
            }

            foreach (
                var expression in
                    objectCreateExpression.ObjectInitializer.CreateExpressions.OfType<MemberReferenceExpression>())
            {
                FieldNames.Add(expression.MemberName);
            }
        }
    }
}