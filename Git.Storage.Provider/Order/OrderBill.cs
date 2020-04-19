/*******************************************************************************
 * Copyright (C) Git Corporation. All rights reserved.
 *
 * Author: 情缘
 * Create Date: 2017-01-01 14:38:43
 *
 * Description: Git.Framework
 * http://www.cnblogs.com/qingyuan/
 * 吉特云仓储：http://yun.gitwms.com/
 * 吉特仓储系统:http://www.gitwms.com/
 * 
 * Revision History:
 * Date         Author               Description
 * 2013-09-01 15:10:06       情缘
*********************************************************************************/

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Transactions;
using Git.Framework.DataTypes;
using Git.Framework.DataTypes.ExtensionMethods;
using Git.Framework.ORM;
using Git.Storage.Common;
using Git.Storage.Entity.Base;
using Git.Storage.Entity.Order;
using Git.Storage.Entity.Store;
using Git.Storage.Provider.Base;

namespace Git.Storage.Provider.Order {
    public class OrderBill : Bill<OrdersEntity, OrderDetailEntity> {
        /// <summary>
        ///     创建单据
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="list"></param>
        /// <returns></returns>
        public override string Create(OrdersEntity entity, List<OrderDetailEntity> list) {
            using (var ts = new TransactionScope()) {
                var line = 0;
                entity.OrderNum = entity.OrderNum.IsEmpty()
                    ? SequenceProvider.GetSequence(typeof(OrdersEntity))
                    : entity.OrderNum;
                entity.IncludeAll();

                if (!list.IsNullOrEmpty()) {
                    list.ForEach(a => {
                        a.OrderNum = entity.OrderNum;
                        a.SendTime = entity.SendDate;
                        a.IncludeAll();
                    });
                    line = Orders.Add(entity);
                    line += OrderDetail.Add(list);
                }

                ts.Complete();
                return line > 0 ? EnumHelper.GetEnumDesc(EReturnStatus.Success) : string.Empty;
            }
        }

        /// <summary>
        ///     取消单据
        /// </summary>
        /// <param name="entity"></param>
        /// <returns></returns>
        public override string Cancel(OrdersEntity entity) {
            //只有待审核状态的单据才能取消，已经成功的订单不能取消
            var checkOrder = new OrdersEntity();
            entity.Where(a => a.AuditeStatus == (int) EAudite.Wait).And(a => a.OrderNum == entity.OrderNum);
            if (Orders.GetCount(checkOrder) > 0) return EnumHelper.GetEnumDesc(EReturnStatus.Pass); //已经审核或者取消的订单不能审核
            entity.AuditeStatus = (int) EAudite.NotPass;
            entity.IncludeAuditeStatus(true);
            entity.Where(a => a.OrderNum == entity.OrderNum);
            var line = Orders.Update(entity);
            return line > 0 ? EnumHelper.GetEnumDesc(EReturnStatus.Success) : string.Empty;
        }

        /// <summary>
        ///     删除单据
        /// </summary>
        /// <param name="entity"></param>
        /// <returns></returns>
        public override string Delete(OrdersEntity entity) {
            using (var ts = new TransactionScope()) {
                entity.IsDelete = (int) EIsDelete.Deleted;
                entity.IncludeIsDelete(true);
                entity.Where(a => a.OrderNum == entity.OrderNum);
                var line = Orders.Update(entity);

                var detail = new OrderDetailEntity();
                detail.Where(a => a.OrderNum == entity.OrderNum);

                line += OrderDetail.Delete(detail);
                ts.Complete();
                return line > 0 ? EnumHelper.GetEnumDesc(EReturnStatus.Success) : string.Empty;
            }
        }

        /// <summary>
        ///     审核单据
        /// </summary>
        /// <param name="entity"></param>
        /// <returns></returns>
        public override string Audite(OrdersEntity entity) {
            var orderNum = entity.OrderNum;
            if (entity.AuditeStatus == (int) EAudite.NotPass) {
                entity.IncludeAuditeStatus(true).IncludeReason(true).Where(a => a.OrderNum == entity.OrderNum);
                var line = Orders.Update(entity);
                return line > 0 ? "1000" : string.Empty;
            }

            if (entity.AuditeStatus == (int) EAudite.Pass)
                using (var ts = new TransactionScope()) {
                    entity.IncludeAuditeStatus(true).IncludeReason(true).Where(a => a.OrderNum == orderNum);
                    var line = Orders.Update(entity);
                    ts.Complete();
                    return line > 0 ? "1000" : string.Empty;
                }

            return string.Empty;
        }

        /// <summary>
        ///     打印单据
        /// </summary>
        /// <param name="entity"></param>
        /// <returns></returns>
        public override string Print(OrdersEntity entity) {
            throw new NotImplementedException();
        }

        /// <summary>
        ///     查询单据
        /// </summary>
        /// <param name="entity"></param>
        /// <returns></returns>
        public override OrdersEntity GetOrder(OrdersEntity entity) {
            entity.IncludeAll();
            var admin = new AdminEntity();
            admin.Include(a => new {CreateUserName = a.UserName});
            entity.Left(admin, new Params<string, string> {Item1 = "CreateUser", Item2 = "UserCode"});
            entity.Where(a => a.OrderNum == entity.OrderNum).And(a => a.IsDelete == (int) EIsDelete.NotDelete);
            entity = Orders.GetSingle(entity);
            return entity;
        }

        /// <summary>
        ///     获得单据详细信息
        /// </summary>
        /// <param name="entity"></param>
        /// <returns></returns>
        public override List<OrderDetailEntity> GetOrderDetail(OrderDetailEntity entity) {
            var detail = new OrderDetailEntity();
            detail.IncludeAll();
            detail.Where(a => a.OrderNum == entity.OrderNum);
            var product = new ProductEntity();
            product.Include(a => new {a.Size, a.UnitName});
            detail.Left(product, new Params<string, string> {Item1 = "ProductNum", Item2 = "SnNum"});
            var list = OrderDetail.GetList(detail);

            return list;
        }

        /// <summary>
        ///     查询单据分页
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="pageInfo"></param>
        /// <returns></returns>
        public override List<OrdersEntity> GetList(OrdersEntity entity, ref PageInfo pageInfo) {
            entity.IncludeAll();
            entity.Where(a => a.IsDelete == (int) EIsDelete.NotDelete);
            entity.OrderBy(a => a.ID, EOrderBy.DESC);
            var admin = new AdminEntity();
            admin.Include(a => new {CreateUserName = a.UserName});
            entity.Left(admin, new Params<string, string> {Item1 = "CreateUser", Item2 = "UserCode"});
            var detail = new OrderDetailEntity();
            detail.Include(a => new {a.BarCode, a.ProductNum, a.ProductName});
            entity.Left(detail, new Params<string, string> {Item1 = "SnNum", Item2 = "OrderSnNum"});
            var rowCount = 0;
            var listResult = Orders.GetList(entity, pageInfo.PageSize, pageInfo.PageIndex, out rowCount);
            pageInfo.RowCount = rowCount;
            return listResult;
        }

        /// <summary>
        ///     查询单据详细数据分页
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="pageInfo"></param>
        /// <returns></returns>
        public override List<OrderDetailEntity> GetDetailList(OrderDetailEntity entity, ref PageInfo pageInfo) {
            var detail = new OrderDetailEntity();
            detail.Where(a => a.OrderNum == entity.OrderNum);
            detail.IncludeAll();
            detail.OrderBy(a => a.ID, EOrderBy.DESC);
            var rowCount = 0;
            var listResult = OrderDetail.GetList(detail, pageInfo.PageSize, pageInfo.PageIndex, out rowCount);
            pageInfo.RowCount = rowCount;
            return listResult;
        }

        /// <summary>
        ///     编辑单据信息
        /// </summary>
        /// <param name="entity"></param>
        /// <returns></returns>
        public override string EditOrder(OrdersEntity entity) {
            entity.Include(a => new {a.OrderType, a.ContractOrder, a.Remark, a.Amount, a.Num});
            entity.Where(a => a.OrderNum == entity.OrderNum);
            var line = Orders.Update(entity);
            return line > 0 ? EnumHelper.GetEnumDesc(EReturnStatus.Success) : string.Empty;
        }

        /// <summary>
        ///     编辑单据详细信息
        /// </summary>
        /// <param name="entity"></param>
        /// <returns></returns>
        public override string EditDetail(OrderDetailEntity entity) {
            entity.Where(a => a.SnNum == entity.SnNum);
            var line = OrderDetail.Update(entity);
            return line > 0 ? EnumHelper.GetEnumDesc(EReturnStatus.Success) : string.Empty;
        }

        /// <summary>
        ///     编辑订单
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="list"></param>
        /// <returns></returns>
        public override string EditOrder(OrdersEntity entity, List<OrderDetailEntity> list) {
            using (var ts = new TransactionScope()) {
                var line = 0;
                entity.Include(a => new {a.OrderType, a.ContractOrder, a.Remark, a.Amount, a.Num});
                entity.Where(a => a.OrderNum == entity.OrderNum);
                var detail = new OrderDetailEntity();
                detail.Where(a => a.OrderNum == entity.OrderNum);
                MoveOrderDetail.Delete(detail);
                foreach (var item in list) {
                    item.OrderNum = entity.OrderNum;
                    item.IncludeAll();
                }

                entity.Num = list.Sum(a => a.Num);
                entity.Amount = list.Sum(a => a.Amount);
                line = Orders.Update(entity);
                OrderDetail.Add(list);
                ts.Complete();
                return line > 0 ? EnumHelper.GetEnumDesc(EReturnStatus.Success) : string.Empty;
            }
        }

        /// <summary>
        ///     获得订单数量
        /// </summary>
        /// <param name="entity"></param>
        /// <returns></returns>
        public override int GetCount(OrdersEntity entity) {
            return Orders.GetCount(entity);
        }


        /// <summary>
        ///     获得打印单据的数据源
        /// </summary>
        /// <param name="argOrderNum"></param>
        /// <returns></returns>
        public override DataSet GetPrint(string argOrderNum) {
            var ds = new DataSet();
            var entity = new OrdersEntity();
            entity.OrderNum = argOrderNum;
            entity = GetOrder(entity);
            if (entity != null) {
                var list = new List<OrdersEntity>();
                list.Add(entity);
                var tableOrder = list.ToDataTable();
                ds.Tables.Add(tableOrder);

                var detail = new OrderDetailEntity();
                detail.OrderNum = argOrderNum;
                var listDetail = GetOrderDetail(detail);
                if (!listDetail.IsNullOrEmpty()) {
                    var tableDetail = listDetail.ToDataTable();
                    ds.Tables.Add(tableDetail);
                }
            }

            return ds;
        }
    }
}