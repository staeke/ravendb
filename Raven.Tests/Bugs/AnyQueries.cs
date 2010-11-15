using System;
using System.Linq;
using System.Collections.Generic;
using Raven.Client;
using Xunit;

namespace Raven.Tests.Bugs
{
   public class RavenDbAnyOfPropertyCollection : LocalClientTest, IDisposable
   {
       readonly IDocumentStore store;
       DateTime now = new DateTime(2010, 10, 31);

       public RavenDbAnyOfPropertyCollection()
       {
           store = NewDocumentStore();
           using(var session = store.OpenSession())
           {
              session.Store(new Account
              {
                  Transactions =
                      {
                          new Transaction(1, now.AddDays(-2)),
                          new Transaction(3, now.AddDays(-1)),
                      }
              });
              session.Store(new Account
              {
                  Transactions =
                      {
                          new Transaction(2, now.AddDays(1)),
                          new Transaction(4, now.AddDays(2)),
                      }
              });
               session.SaveChanges();
           }
       }

       [Fact]
       public void ShouldBeAbleToQueryOnTransactionAmount()
       {
           using(var session = store.OpenSession())
           {
               var accounts = session.Query<Account>()
                   .Where(x => x.Transactions.Any(y => y.Amount == 2));
               Assert.Equal(accounts.Count(), 1);
           }
       }

       [Fact]
       public void InequalityOperatorDoesNotWorkOnAny()
       {
           using(var session = store.OpenSession())
           {
               var accounts = session.Query<Account>().Where(x => x.Transactions.Any(y => y.Amount < 3));
               Assert.Equal(accounts.Count(), 2);
           }
       }


       [Fact]
       public void InequalityOperatorDoesNotWorkOnWhereThenAny()
       {
           using(var session = store.OpenSession())
           {
               var accounts = session.Query<Account>().Where(x => x.Transactions.Any(y => y.Amount <= 2));
               Assert.Equal(accounts.Count(), 2);
           }
       }

      [Fact]
      public void CanSelectADateRange()
      {
          using(var session = store.OpenSession())
          {
              var accounts = session.Query<Account>().Where(x => x.Transactions.Any(y => y.Date < now));
              var array = accounts.ToArray();
              Assert.Equal(1, array.Count());
          }
      }
       public void Dispose()
       {
           if (store != null) store.Dispose();
       }

   }

   public class Account
   {
       public Account()
       {
           Transactions = new List<Transaction>();
       }

       public IList<Transaction> Transactions { get; private set; }
   }

   public class Transaction
   {
       public Transaction(int amount, DateTime date)
       {
           Amount = amount;
           Date = date;
       }

       public Transaction()
       {
           
       }

       public int Amount { get; private set; }
       public DateTime Date { get; private set; }
   }
}